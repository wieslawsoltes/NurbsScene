using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Parametric;
using NurbsScene.Presentation;

namespace NurbsScene.Controls;

public sealed partial class GraphEditorControl
{
    private PanelTypographyDialog? _panelTypographyDialog;
    public FrameworkElement? PanelTypographyEditor => _panelTypographyDialog;
    public bool PanelTypographyEditorIsOpen => _panelTypographyDialog?.IsLoaded == true;
    public string PanelTypographyValidation => _panelTypographyDialog?.ValidationMessage ?? "";
    private void QueuePanelTypography(Graph expected, string id) => DispatcherQueue.TryEnqueue(() => {
        if (!_valueEditorDisposed && ReferenceEquals(Session?.Graph, expected)) _ = EditPanelTypographyAsync(expected, id);
    });
    private async Task EditPanelTypographyAsync(Graph expected, string id)
    {
        if (_valueEditorDisposed || Session is null || !ReferenceEquals(Session.Graph, expected) || XamlRoot is null ||
            _panelTypographyDialog is not null || _textPanelDialog is not null || _numberSliderDialog is not null || Canvas.IsInteracting) return;
        var session = Session;
        try
        {
            var node = expected.GetNode(id); CloseActionPopups();
            var dialog = new PanelTypographyDialog(node) { XamlRoot = XamlRoot }; _panelTypographyDialog = dialog;
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Edit is { } edit)
            {
                if (_valueEditorDisposed || !ReferenceEquals(Session, session)) throw new InvalidOperationException("The definition session changed. Reopen the typography editor.");
                PanelTypographyEditing.Apply(session, expected, node, edit); RefreshInspector(); UpdateStatus();
            }
        }
        catch (Exception error) { if (!_valueEditorDisposed) Report(error.Message); }
        finally { _panelTypographyDialog = null; if (!_valueEditorDisposed) Canvas.Focus(FocusState.Programmatic); }
    }
    private void AddTypographyMenus(MenuFlyout menu, Graph graph, GraphNode node)
    {
        var item = new MenuFlyoutItem { Text = "Typography…  Ctrl+double-click", FontSize = 11, MinHeight = 24, Padding = new(8,2,8,2) };
        AutomationProperties.SetAutomationId(item, "GraphPanel_Typography"); _contextElements.Add(item); menu.Items.Add(item);
        item.Click += (_,_) => QueuePanelTypography(graph, node.Id);
        var align = new MenuFlyoutSubItem { Text = "Text alignment", FontSize = 11, MinHeight = 24, Padding = new(8,2,8,2) };
        AutomationProperties.SetAutomationId(align, "GraphPanel_Alignment"); _contextElements.Add(align); menu.Items.Add(align);
        var current = PanelTextStyle.For(node);
        void Add(string label, string id, bool selected, PanelTextStyle style)
        {
            var option = new ToggleMenuFlyoutItem { Text = label, IsChecked = selected, FontSize = 11, MinHeight = 24, Padding = new(8,2,8,2) };
            AutomationProperties.SetAutomationId(option, id); _contextElements.Add(option); align.Items.Add(option);
            option.Click += (_,_) => { try { if (!_valueEditorDisposed && Session is not null) { PanelTypographyEditing.Apply(Session, graph, node, style); RefreshInspector(); UpdateStatus(); } } catch (Exception error) { Report(error.Message); } };
        }
        foreach (var horizontal in Enum.GetValues<PanelHorizontalAlignment>()) Add(horizontal.ToString(), "GraphPanel_H" + horizontal, current.Horizontal == horizontal, current with { Horizontal = horizontal });
        align.Items.Add(new MenuFlyoutSeparator());
        foreach (var vertical in Enum.GetValues<PanelVerticalAlignment>()) Add(vertical.ToString(), "GraphPanel_V" + vertical, current.Vertical == vertical, current with { Vertical = vertical });
    }
}

public sealed partial class GraphCanvas
{
    private readonly PanelTypographyRenderer _panelTypography = new();
    public event Action<string>? PanelTypographyRequested;
    public PanelRenderedTypography? RenderedTypography(string id) => _panelTypography.Observe(id);
    private PanelPaintStamp TypographySource(GraphNode node, NodeResult? result) => new(_session!.Graph, node, result,
        _zoom, _panelResize?.NodeId == node.Id ? _panelResize.Bounds : PanelLayout.Bounds(node)) { ScrollOffset = PanelOffset(node.Id) };
    public bool TypographyIsCurrent(string id)
    {
        if (_session is null || !_session.Graph.TryGetNode(id, out var node) || node.Component is not ("Panel" or "Text")) return false;
        var result = _session.Graph.LastResult?.Nodes.GetValueOrDefault(id);
        return result is not null && _panelTypography.IsCurrent(id, TypographySource(node, result));
    }
    public long PanelGlyphBuilds => _panelTypography.GlyphBuilds;
    public long PanelMeasurementBuilds => _panelTypography.MeasurementBuilds;
    public long PanelPlacementBuilds => _panelTypography.PlacementBuilds;
}
