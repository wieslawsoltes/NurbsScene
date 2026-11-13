#if NURBSSCENE_QUALIFICATION
using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NurbsScene.Controls;

namespace NurbsScene.App;

public partial class App
{
    // Observe the native text/layout that is actually on screen. These values
    // are not reconstructed from the console's intended row sizes or fonts.
    private static object ObserveCommandConsole(WorkspaceView view)
    {
        T? Find<T>(DependencyObject parent, Func<T, bool> matches) where T : DependencyObject
        {
            if (parent is T value && matches(value)) return value;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                if (Find(VisualTreeHelper.GetChild(parent, i), matches) is { } found) return found;
            return null;
        }

        T? Named<T>(string name) where T : DependencyObject =>
            Find<T>(view.Console, element => AutomationProperties.GetAutomationId(element) == name);

        object? Bounds(FrameworkElement? element)
        {
            if (element is null) return null;
            var origin = element.TransformToVisual(view).TransformPoint(new(0, 0));
            return new
            {
                x = origin.X, y = origin.Y, width = element.ActualWidth, height = element.ActualHeight,
                desiredWidth = element.DesiredSize.Width, desiredHeight = element.DesiredSize.Height,
                margin = new { left = element.Margin.Left, top = element.Margin.Top,
                    right = element.Margin.Right, bottom = element.Margin.Bottom },
                loaded = element.IsLoaded, visible = element.Visibility == Visibility.Visible
            };
        }

        object? Text(TextBlock? element) => element is null ? null : new
        {
            bounds = Bounds(element), text = element.Text, fontSize = element.FontSize,
            fontWeight = element.FontWeight.Weight, baselineOffset = element.BaselineOffset,
            lineHeight = element.LineHeight, lineStacking = element.LineStackingStrategy.ToString(),
            wrapping = element.TextWrapping.ToString(), selectionEnabled = element.IsTextSelectionEnabled,
            selectedLength = element.SelectedText.Length,
            padding = new { left = element.Padding.Left, top = element.Padding.Top,
                right = element.Padding.Right, bottom = element.Padding.Bottom }
        };

        var history = Named<ScrollViewer>("CommandHistory");
        var historyText = Named<TextBlock>("CommandHistoryText");
        var prompt = Named<TextBlock>("CommandPrompt");
        var input = Named<AutoSuggestBox>("CommandInput");
        var inner = input is null ? null : Find<TextBox>(input, _ => true);
        // Uno's TextBoxView uses a native TextBlock for its visible text even
        // while the invisible browser input owns editing/IME. Read that actual
        // descendant, rather than the unimplemented character-rectangle API.
        var renderedInput = inner is null ? null : Find<TextBlock>(inner,
            element => element.IsLoaded && element.ActualWidth > 0 && element.ActualHeight > 0
                && element.Text == inner.Text && element.FontSize == inner.FontSize);
        return new
        {
            bounds = Bounds(view.Console), entry = Bounds(Named<FrameworkElement>("CommandEntryRow")),
            historyExpanded = view.Console.HistoryExpanded, text = view.Console.HistoryText,
            prompt = Text(prompt), historyText = Text(historyText), renderedInput = Text(renderedInput),
            input = input is null ? null : new
            {
                bounds = Bounds(input), text = input.Text, placeholder = input.PlaceholderText,
                fontSize = input.FontSize, fontWeight = input.FontWeight.Weight,
                suggestionListOpen = input.IsSuggestionListOpen,
                inner = inner is null ? null : new
                {
                    bounds = Bounds(inner), text = inner.Text, placeholder = inner.PlaceholderText,
                    fontSize = inner.FontSize, fontWeight = inner.FontWeight.Weight,
                    padding = new { left = inner.Padding.Left, top = inner.Padding.Top,
                        right = inner.Padding.Right, bottom = inner.Padding.Bottom },
                    selectionStart = inner.SelectionStart, selectionLength = inner.SelectionLength
                }
            },
            history = history is null ? null : new
            {
                bounds = Bounds(history), viewportWidth = history.ViewportWidth,
                viewportHeight = history.ViewportHeight, extentWidth = history.ExtentWidth,
                extentHeight = history.ExtentHeight, horizontalOffset = history.HorizontalOffset,
                verticalOffset = history.VerticalOffset, scrollableHeight = history.ScrollableHeight,
                horizontalScrollBar = history.HorizontalScrollBarVisibility.ToString(),
                verticalScrollBar = history.VerticalScrollBarVisibility.ToString()
            }
        };
    }

    // Only the existing rhino-workspace campaign opts into this checkpoint. It
    // pauses before any modeling fixture is loaded so the runner can capture
    // ordinary startup. The flag releases that existing fixture; it exposes no
    // command, geometry, focus, or application-state mutation API.
    private static async Task<object> ObserveOrdinaryStartupAsync(WorkspaceView view)
    {
        var watch = Stopwatch.StartNew();
        long sample = 0;
        object? latest = null;
        void Publish(object observation)
        {
            var encoded = JsonSerializer.Serialize(JsonSerializer.Serialize(observation));
            Uno.Foundation.WebAssemblyRuntime.InvokeJS($"globalThis.nurbsSceneStartup = JSON.parse({encoded});");
        }

        while (watch.Elapsed < TimeSpan.FromSeconds(20))
        {
            latest = new
            {
                status = "ready", sample = ++sample, console = ObserveCommandConsole(view),
                state = new
                {
                    objects = view.Controller.Document.Objects.Count, nodes = view.Controller.Graph.Nodes.Count,
                    history = view.Console.HistoryText, preferences = view.Preferences.Encode(),
                    frames = view.Viewport.Canvas.Frames, gpuError = view.Viewport.Canvas.LastError,
                    workspaceWidth = view.ActualWidth, workspaceHeight = view.ActualHeight
                }
            };
            Publish(latest);
            if (Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.nurbsSceneStartupContinue === true ? 'yes' : 'no'") == "yes")
                return latest;
            await Task.Delay(120);
        }

        const string message = "Ordinary startup observation expired before the browser captured and released the checkpoint.";
        Publish(new { status = "failed", error = message });
        throw new TimeoutException(message);
    }
}
#endif
