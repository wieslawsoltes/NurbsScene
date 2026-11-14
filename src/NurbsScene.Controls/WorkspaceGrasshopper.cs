using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using Windows.System;

namespace NurbsScene.Controls;

public sealed partial class WorkspaceView
{
    private bool _definitionFocusRequested, _definitionSearchRequested;
    private (Graph Graph, string OutputId)? _examplePreviewToFrame;
    public bool IsGrasshopperOpenPending => _definitionFocusRequested;

    private Button CreateGrasshopperLauncher()
    {
        var button = ClassicChrome.Button("Grasshopper", () => Run("Grasshopper"), "OpenGrasshopper");
        button.Height = 20;
        button.Padding = new(7, 0, 7, 0);
        button.BorderThickness = new(1, 0, 0, 0);
        button.Background = ClassicChrome.Brush(231, 237, 220);
        var caption = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        caption.Children.Add(new WorkbenchIcon("Graph") { Width = 15, Height = 15 });
        caption.Children.Add(ClassicChrome.Label("Grasshopper", 11));
        button.Content = caption;
        AutomationProperties.SetHelpText(button, "Open the visual programming editor. Ctrl+Alt+G.");
        ToolTipService.SetToolTip(button, "Open Grasshopper · Visual programming (Ctrl+Alt+G)");
        return button;
    }

    private void AddGrasshopperMenuItems(MenuBarItem menu)
    {
        void Command(string text, string command, string id) =>
            menu.Items.Add(ShellItem(text, () => Run(command), id));
        Command("Open Grasshopper (Ctrl+Alt+G)", "Grasshopper", "Menu_Grasshopper_Open");
        Command("Add a component…", "GrasshopperSearch", "Menu_Grasshopper_AddComponent");
        Command("Insert extrusion example", "GrasshopperExample", "Menu_Grasshopper_InsertExample");
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ShellItem("Open workspace…", () => _ = SafeAsync(OpenAsync), "Menu_Grasshopper_OpenWorkspace"));
        menu.Items.Add(ShellItem("Save workspace…", () => _ = SafeAsync(SaveAsync), "Menu_Grasshopper_SaveWorkspace"));
        menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var (dock, label) in new[] { ("Right", "Dock right"), ("Bottom", "Dock below viewports"), ("Float", "Floating window") })
        {
            var target = dock;
            menu.Items.Add(ShellItem(label, () => OpenGrasshopper(target), "Menu_Grasshopper_Dock" + target));
        }
        Command("Hide Grasshopper", "HideGrasshopper", "Menu_Grasshopper_Hide");
    }

    private void ConfigureGrasshopperShortcut()
    {
        var accelerator = new KeyboardAccelerator { Key = VirtualKey.G, Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu };
        accelerator.Invoked += (_, e) => { Run("Grasshopper"); e.Handled = true; };
        KeyboardAccelerators.Add(accelerator);
    }

    private void OpenGrasshopper(string? dock = null, bool search = false)
    {
        if (_disposed) return;
        // The dock operation cancels a live floating drag before preserving its
        // committed placement. Never reset unrelated workspace preferences here.
        HandleDefinitionDock(dock ?? Preferences.Dock);
        _definitionFocusRequested = true;
        _definitionSearchRequested = search;
        QueueLayoutFit();
    }

    private void CompleteGrasshopperOpen()
    {
        if (!_definitionFocusRequested) return;
        var search = _definitionSearchRequested;
        CancelGrasshopperOpen();
        GraphEditor.ActivateCanvas(search);
    }

    private void CancelGrasshopperOpen()
    {
        _definitionFocusRequested = false;
        _definitionSearchRequested = false;
    }

    private void InsertGrasshopperExample()
    {
        OpenGrasshopper();
        GraphEditor.Canvas.CancelInteraction();
        var graph = Controller.Graph;
        var output = GrasshopperExamples.InsertExtrusion(Controller.GraphSession);
        _examplePreviewToFrame = (graph, output);
        GraphEditor.Canvas.SelectNodes([output]);
        _fitAfterLayout = true;
        QueueLayoutFit();
        FrameGrasshopperExamplePreview();
    }

    private void FrameGrasshopperExamplePreview()
    {
        if (_examplePreviewToFrame is not { } pending) return;
        if (!ReferenceEquals(Controller.Graph, pending.Graph) || !pending.Graph.TryGetNode(pending.OutputId, out _))
        {
            _examplePreviewToFrame = null;
            return;
        }
        // Solving can complete before the dock resize. Fit only after that same
        // native layout pass has made both the graph and model view usable.
        if (_definitionFocusRequested || Viewport.Canvas.ActualWidth <= 0 || Viewport.Canvas.ActualHeight <= 0) return;
        // Use the normal debounced solve and preview publication. A second
        // manual solve could supersede it; frame only its completed current result.
        var result = pending.Graph.LastResult;
        if (result is null || result.Stale || result.Revision != pending.Graph.Revision) return;
        _examplePreviewToFrame = null;
        if (result.Nodes.TryGetValue(pending.OutputId, out var output) && output.Status == "ok")
            Viewport.Canvas.Fit();
    }
}
