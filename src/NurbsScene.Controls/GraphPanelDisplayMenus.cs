using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Parametric;
using NurbsScene.Presentation;

namespace NurbsScene.Controls;

public sealed partial class GraphEditorControl
{
    private void AddPanelDisplayMenus(MenuFlyout menu, Graph expected, GraphNode node)
    {
        if (node.Component is not ("Panel" or "Text")) return;
        var options = PanelDisplayOptions.For(node);
        void Toggle(string label, string id, bool selected, Func<PanelDisplayOptions, PanelDisplayOptions> change)
        {
            var item = new ToggleMenuFlyoutItem { Text = label, IsChecked = selected,
                FontSize = 11, MinHeight = 24, Padding = new(8,2,8,2) };
            AutomationProperties.SetAutomationId(item, "GraphPanel_" + id); _contextElements.Add(item);
            item.Click += (_, _) =>
            {
                if (_valueEditorDisposed || Session is null) return;
                try
                {
                    PanelDisplayEditing.Apply(Session, expected, node, change(options));
                    RefreshInspector(); UpdateStatus();
                }
                catch (Exception error) { Report(error.Message); }
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        Toggle("Draw paths", "DrawPaths", options.DrawPaths, o => o with { DrawPaths = !o.DrawPaths });
        Toggle("Draw indices", "DrawIndices", options.DrawIndices, o => o with { DrawIndices = !o.DrawIndices });
        Toggle("Wrap items", "WrapItems", options.WrapItems, o => o with { WrapItems = !o.WrapItems });
        AddTypographyMenus(menu, expected, node);
    }
}

public sealed partial class GraphCanvas
{
    /// <summary>The immutable preview from the most recent actual panel draw.
    /// Observation does not shape text, evaluate a component or invalidate paint.</summary>
    public PanelPreview? GetRenderedPanelPreview(string nodeId)
        => _panelWindows.TryGetValue(nodeId, out var preview) ? preview.Current?.Preview : null;
}
