#if NURBSSCENE_QUALIFICATION
using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NurbsScene.Controls;
using NurbsScene.Geometry;

namespace NurbsScene.App;

public partial class App
{
    // Observe ordinary native chrome before the pavilion fixture opens the
    // editor. Labels are read from the rendered TextBlocks, not inferred from
    // automation names or the command catalog. Hidden ancestors stay hidden.
    private static object ObserveGrasshopperWorkspace(WorkspaceView view)
    {
        var controls = new Dictionary<string, object>();
        var focused = view.XamlRoot is { } root ? FocusManager.GetFocusedElement(root) : null;
        bool Visible(DependencyObject value)
        {
            for (DependencyObject? current = value; current is not null; current = VisualTreeHelper.GetParent(current))
                if (current is UIElement element && element.Visibility != Visibility.Visible) return false;
            return true;
        }
        void Labels(DependencyObject value, List<string> labels)
        {
            if (value is FrameworkElement element && (!element.IsLoaded || !Visible(element))) return;
            if (value is TextBlock text && text.ActualWidth > 0 && text.ActualHeight > 0 && text.Text.Length > 0)
                labels.Add(text.Text);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(value); i++) Labels(VisualTreeHelper.GetChild(value, i), labels);
        }
        void Visit(DependencyObject value)
        {
            if (value is FrameworkElement element)
            {
                if (!element.IsLoaded || !Visible(element)) return;
                var id = AutomationProperties.GetAutomationId(element);
                if ((id is "OpenGrasshopper" or "Tool_Definition" or "ModelMenu_Grasshopper" or
                    "GraphGettingStarted" or "GraphStartAddComponent" or "GraphStartExample" or
                    "ParametricCanvas" or "GraphFloatFrame" or "GraphFold" or "GraphHide") &&
                    element.ActualWidth > 0 && element.ActualHeight > 0)
                {
                    var point = element.TransformToVisual(view).TransformPoint(new(0, 0));
                    var labels = new List<string>();
                    Labels(element, labels);
                    controls[id] = new
                    {
                        x = point.X, y = point.Y, width = element.ActualWidth, height = element.ActualHeight,
                        enabled = element is not Control control || control.IsEnabled,
                        focused = ReferenceEquals(element, focused), labels
                    };
                }
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(value); i++) Visit(VisualTreeHelper.GetChild(value, i));
        }
        Visit(view);
        var controller = view.Controller;
        var graph = view.GraphEditor.Canvas;
        return new
        {
            controls,
            state = new
            {
                objects = controller.Document.Objects.Count, nodes = controller.Graph.Nodes.Count,
                documentRevision = controller.Document.Revision, graphRevision = controller.Graph.Revision,
                documentUndo = controller.Document.CanUndo, documentRedo = controller.Document.CanRedo,
                graphUndo = controller.GraphSession.CanUndo, graphRedo = controller.GraphSession.CanRedo,
                history = view.Console.HistoryText, preferences = view.Preferences.Encode(),
                definitionVisible = view.Preferences.DefinitionVisible, definitionFolded = view.DefinitionFolded,
                editorFolded = view.GraphEditor.IsFolded, definitionFloating = view.DefinitionFloating,
                openPending = view.IsGrasshopperOpenPending,
                gettingStartedVisible = view.GraphEditor.GettingStartedVisible,
                graphFocused = ReferenceEquals(focused, graph), graphFocus = graph.FocusState.ToString(),
                graphFrames = graph.DrawingFrames, graphError = graph.LastDrawingError,
                frames = view.Viewport.Canvas.Frames, gpuError = view.Viewport.Canvas.LastError,
                workspaceWidth = view.ActualWidth, workspaceHeight = view.ActualHeight
            }
        };
    }

    // This independently bounded native-input phase starts only after the
    // untouched startup screenshot is acknowledged. It does not renew the
    // existing 20-second screenshot guard or 15-minute campaign lifetime.
    // The flag only releases the existing fixture; all edits/focus come from
    // the runner's ordinary mouse and keyboard events.
    private static async Task<object> ObserveGrasshopperDiscoveryAsync(WorkspaceView view)
    {
        var watch = Stopwatch.StartNew();
        long sample = 0;
        void Publish(object value)
        {
            var encoded = JsonSerializer.Serialize(JsonSerializer.Serialize(value));
            Uno.Foundation.WebAssemblyRuntime.InvokeJS($"globalThis.nurbsSceneGrasshopperDiscovery = JSON.parse({encoded});");
        }
        while (watch.Elapsed < TimeSpan.FromSeconds(20))
        {
            var snapshot = new { status = "ready", sample = ++sample, workspace = ObserveGrasshopperWorkspace(view) };
            Publish(snapshot);
            if (Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.nurbsSceneGrasshopperDiscoveryContinue === true ? 'yes' : 'no'") == "yes")
                return snapshot;
            await Task.Delay(120);
        }
        const string message = "Native Grasshopper discovery expired before its input phase was released.";
        Publish(new { status = "failed", error = message });
        throw new TimeoutException(message);
    }

    // Constant-size samples of actual selected graph outputs and their scene
    // entries. This never creates geometry, changes preview, or asks for a draw.
    private static object ObserveSelectedGraphGeometry(WorkspaceView view)
    {
        var graph = view.Controller.Graph;
        var scene = view.Controller.Scene;
        var selected = view.GraphEditor.Canvas.Selection;
        var outputs = new List<object>();
        foreach (var node in graph.Nodes.Where(node => selected.Contains(node.Id)).Take(8))
        {
            if (graph.LastResult?.Nodes.GetValueOrDefault(node.Id) is not { } result) continue;
            foreach (var (port, tree) in result.Outputs.Take(8))
            {
                var index = 0;
                foreach (var geometry in tree.AllItems.OfType<IGeometry>().Take(4))
                {
                    var previewId = $"preview:{node.Id}:{port}:{index++}";
                    scene.Meshes.TryGetValue(geometry.Id, out var mesh);
                    object? shape = geometry switch
                    {
                        NurbsSurface surface => new
                        {
                            kind = "surface", degreeU = surface.DegreeU, degreeV = surface.DegreeV,
                            samples = Enumerable.Range(0, 9).Select(i => surface.PointAt(
                                surface.DomainU.At((i % 3) / 2d), surface.DomainV.At((i / 3) / 2d)).ToArray()).ToArray()
                        },
                        NurbsCurve curve => new
                        {
                            kind = "curve", degreeU = curve.Degree, degreeV = 0,
                            samples = Enumerable.Range(0, 9).Select(i => curve.PointAt(curve.Domain.At(i / 8d)).ToArray()).ToArray()
                        },
                        _ => null
                    };
                    outputs.Add(new
                    {
                        node = node.Id, port, geometryId = geometry.Id, shape,
                        sceneIds = scene.Items.Where(item => item.Id == previewId && ReferenceEquals(item.Geometry, geometry)).Select(item => item.Id).ToArray(),
                        meshTriangles = mesh?.Indices.Length / 3 ?? 0
                    });
                }
            }
        }
        return new { solvedRevision = graph.LastResult?.Revision, sceneRevision = scene.Revision, outputs };
    }
}
#endif
