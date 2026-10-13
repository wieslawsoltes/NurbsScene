using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using SkiaSharp;

namespace NurbsScene.Controls;

public sealed partial class GraphCanvas
{
    private readonly Dictionary<string, PanelPreviewCache> _panelPreviews = [];
    private readonly Dictionary<string, PanelDisplayLayout> _panelDisplayLayouts = [];
    public PanelDisplayLayout? PanelDrawing(string id) => _panelDisplayLayouts.TryGetValue(id, out var layout) && layout.Readable ? layout : null;
    public long ValuePreviewBuilds { get; private set; }
    public event Action<string>? TextEditRequested;
    private void HandleValueDoubleTap(GraphNode node)
    {
        if (_session is null || node.Component is not ("Panel" or "Text" or "Boolean")) return;
        CancelGesture(); ReleasePointerCaptures(); SelectNode(node.Id);
        if (node.Component is "Panel" or "Text") TextEditRequested?.Invoke(node.Id);
        else if (node.Component == "Boolean")
            try { CanvasValueEditing.Toggle(_session, _session.Graph, node); }
            catch (Exception error) { Message?.Invoke(error.Message); }
    }
    private void DrawBooleanToggle(SKCanvas canvas, GraphNode node, SKRect rect)
    {
        string label;
        try { label = InputComponents.BooleanValue(node.Parameters) ? "True" : "False"; }
        catch (ArgumentException) { label = "Invalid"; }
        Text(canvas, node.Name ?? "Boolean Toggle", rect.Left + 8, rect.Top + 24, new(55,55,61), 10);
        Fill(canvas, new(rect.Right - 56, rect.Top + 5, rect.Right - 7, rect.Bottom - 5), new(80,80,83), 3);
        Text(canvas, label, rect.Right - 50, rect.Top + 24, new(255,255,255), 11);
    }
    private void DrawDataPanel(SKCanvas canvas, GraphNode node, SKRect rect, NodeResult? result)
    {
        var style = PanelTextStyle.For(node);
        var layout = PanelDisplayLayout.Create(new(rect.Left, rect.Top, rect.Width, rect.Height), _zoom, style.Size);
        _panelDisplayLayouts[node.Id] = layout;
        if (!layout.Readable) { _panelTypography.Forget(node.Id); return; }
        if (!_panelPreviews.TryGetValue(node.Id, out var cache)) _panelPreviews[node.Id] = cache = new();
        var builds = cache.BuildCount; var preview = cache.Get(node, result, layout.Rows, layout.Columns); ValuePreviewBuilds += cache.BuildCount - builds;
        var font = (float)layout.FontSize;
        Text(canvas, node.Name ?? (node.Component == "Text" ? "Text" : "Panel"), rect.Left + 8, rect.Top + (float)layout.HeadingBaseline, new(94,92,54), font);
        var drawn = _panelTypography.Draw(canvas, node.Id, preview, style, rect, layout, TypographySource(node, result));
        Text(canvas, drawn.Placement.Clipped && !preview.Truncated ? preview.Footer + " · …" : preview.Footer, rect.Left + 8, rect.Bottom - (float)layout.FooterInset, new(112,105,69), font * .9f);
    }
}

public sealed partial class GraphEditorControl
{
    private readonly CanvasSearchIndex _toolbarSearchIndex = new();
    private const string CreationHint = "Number · min < value < max · //text · True/False · + − * /";
    private readonly TextBlock _creationInfo = new() { Text = CreationHint, FontSize = 10, TextWrapping = TextWrapping.Wrap, MaxWidth = 290 };
    private TextPanelDialog? _textPanelDialog;
    private bool _valueEditorDisposed;
    public FrameworkElement? ValueEditor => _textPanelDialog;
    public string? ValueDraftText => _textPanelDialog?.DraftText;
    public bool ValueEditorIsOpen => _textPanelDialog?.IsLoaded == true;
    public string LastCreationError { get; private set; } = "";
    private void CreationError(string text) { LastCreationError = text; _creationInfo.Text = text; Report(text); }
    private bool CreateFromQuery(string query, CanvasPoint position)
    {
        if (Session is null) return false;
        if (!CanvasCreationParser.TryParse(query, Session.Graph.Registry, out var creation, out var error)) { CreationError(error!); return false; }
        try
        {
            var id = creation!.Apply(Session, position); Canvas.SelectNodes([id]);
            _search.Text = ""; _search.IsSuggestionListOpen = false; LastCreationError = "";
            Canvas.Focus(FocusState.Programmatic); UpdateStatus(); return true;
        }
        catch (Exception e) { CreationError(e.Message); return false; }
    }
    private void InitializeValueEditing()
    {
        AutomationProperties.SetAutomationId(_creationInfo, "CanvasCreationInfo");
        Canvas.PanelTypographyRequested += id => { if (Session is { } s) QueuePanelTypography(s.Graph, id); };
        Canvas.TextEditRequested += id => { if (Session is { } s) QueueTextEdit(s.Graph, id); };
    }
    private void QueueTextEdit(Graph expected, string id) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_valueEditorDisposed && ReferenceEquals(Session?.Graph, expected)) _ = EditTextAsync(expected, id);
    });
    private async Task EditTextAsync(Graph expected, string id)
    {
        if (_valueEditorDisposed || Session is null || !ReferenceEquals(Session.Graph, expected) || _textPanelDialog is not null || _panelTypographyDialog is not null || _numberSliderDialog is not null || XamlRoot is null) return;
        var session = Session;
        try
        {
            var node = expected.GetNode(id); CloseActionPopups();
            var connected = node.Component == "Panel" && expected.Incoming(new(id, "D")).Count > 0;
            _textPanelDialog = new(node, connected) { XamlRoot = XamlRoot };
            if (await _textPanelDialog.ShowAsync() == ContentDialogResult.Primary && !_valueEditorDisposed && ReferenceEquals(Session, session))
            {
                CanvasValueEditing.SetText(session, expected, node, _textPanelDialog.DraftText, _textPanelDialog.SingleItem);
                RefreshInspector(); UpdateStatus();
            }
        }
        catch (Exception error) { if (!_valueEditorDisposed) Report(error.Message); }
        finally { _textPanelDialog = null; if (!_valueEditorDisposed) Canvas.Focus(FocusState.Programmatic); }
    }
    private void ToggleValue(Graph expected, GraphNode node)
    {
        if (Session is null) return;
        try { CanvasValueEditing.Toggle(Session, expected, node); RefreshInspector(); UpdateStatus(); }
        catch (Exception error) { Report(error.Message); }
    }
    private void AddValueContextMenu(MenuFlyout menu, Graph expected, GraphNode node)
    {
        if (node.Component is not ("Panel" or "Text" or "Boolean")) return;
        var item = new MenuFlyoutItem { Text = node.Component == "Boolean" ? "Toggle value" : "Edit text…", FontSize = 11, MinHeight = 24, Padding = new(8,2,8,2) };
        AutomationProperties.SetAutomationId(item, "GraphEditValue"); _contextElements.Add(item);
        item.Click += (_, _) => { if (node.Component == "Boolean") ToggleValue(expected, node); else QueueTextEdit(expected, node.Id); };
        menu.Items.Add(item);
        AddPanelDisplayMenus(menu, expected, node);
    }
    private void AddValueInspector(GraphNode node)
    {
        var expected = Session!.Graph;
        // Keep the component's primary editing action above generic flags and
        // matching options, including in the short bottom-docked inspector.
        _inspector.Children.Insert(Math.Min(2, _inspector.Children.Count), ClassicChrome.Button(node.Component == "Boolean" ? "Toggle value" : "Edit text…",
            () => { if (node.Component == "Boolean") ToggleValue(expected, node); else QueueTextEdit(expected, node.Id); }, "InspectorEditValue"));
    }
    private void DisposeValueEditor() { _valueEditorDisposed = true; _textPanelDialog?.Hide(); _panelTypographyDialog?.Hide(); }
}
