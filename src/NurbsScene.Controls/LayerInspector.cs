using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Model;

namespace NurbsScene.Controls;

/// <summary>A compact native panel container. Model rows retain identity through
/// selection changes; filtering changes only presentation, never document state.</summary>
public sealed partial class LayerInspector : Grid
{
    private readonly ObservableCollection<LayerRow> _rows = [];
    private readonly ObservableCollection<LayerRow> _visibleRows = [];
    private readonly ListView _layers = new() { SelectionMode = ListViewSelectionMode.Single, MinHeight = 60 };
    private readonly ListView _objects = new() { SelectionMode = ListViewSelectionMode.Multiple, MinHeight = 60 };
    private readonly StackPanel _properties = new() { Spacing = 3, Padding = new(8, 4, 8, 8) };
    private readonly Grid _layerPanel = new(), _objectPanel = new();
    private readonly ScrollViewer _propertyScroll;
    private readonly TextBox _layerName = InspectorLayout.Editor("", "Selected layer name", "LayerName");
    private readonly TextBox _layerFilter = InspectorLayout.Editor("", "Filter layers", "LayerFilter");
    private readonly TextBlock _heading = ClassicChrome.Label("Layers", 11);
    private readonly TextBlock _layerCount = ClassicChrome.Label("0 layers", 10), _objectCount = ClassicChrome.Label("0 objects", 10);
    private readonly Dictionary<string, Button> _tabButtons = [];
    private Button? _deleteLayer, _currentLayer, _renameLayer, _colorLayer, _selectLayerObjects;
    private SceneDocument? _document;
    private bool _updating, _filtering;
    private IReadOnlyList<SceneObject>? _objectSnapshot;
    private IReadOnlyList<Layer>? _layerSnapshot;
    private readonly Dictionary<string, InspectorObjectRow> _objectRows = [];
    private string? _selectedLayerId;
    private string _propertyKey = "";
    private SceneObject[] _propertySelection = [];
    private long _layerVersion;
    private InspectorColorDialog? _colorDialog;
    public long LayerStructureChanges { get; private set; }
    public string ActiveTab { get; private set; } = "Layers";
    public string FilterText => _layerFilter.Text;
    public string? SelectedLayerId => (_layers.SelectedItem as LayerRow)?.Id;
    public IReadOnlyList<string> VisibleLayerIds => _visibleRows.Select(r => r.Id).ToArray();
    public FrameworkElement? ColorEditor => _colorDialog;
    public bool ColorEditorIsOpen => _colorDialog?.IsLoaded == true;
    public string ColorEditorValidation => _colorDialog?.ValidationMessage ?? "";
    public event Action<string>? Message;
    public event Action<string>? TabChanged;
    private FrameworkElement? _emptySelectionContent;
    public FrameworkElement? EmptySelectionContent
    {
        get => _emptySelectionContent;
        set
        {
            if (ReferenceEquals(value, _emptySelectionContent)) return;
            _emptySelectionContent = value;
            if (value is ViewportPropertiesPanel viewport) viewport.AttachScrolling(_propertyScroll);
            _propertyKey = ""; Refresh();
        }
    }
    public SceneDocument? Document
    {
        get => _document;
        set
        {
            if (ReferenceEquals(value, _document)) return;
            if (_document is not null) _document.Changed -= OnChanged;
            _colorDialog?.Hide(); _document = value; _rows.Clear(); _visibleRows.Clear(); _objectRows.Clear();
            _selectedLayerId = null; _objectSnapshot = null; _layerSnapshot = null; _propertyKey = ""; _propertySelection = [];
            if (value is not null) value.Changed += OnChanged;
            Refresh();
        }
    }

    public LayerInspector()
    {
        Background = ClassicChrome.PanelBackground; BorderBrush = ClassicChrome.Border; BorderThickness = new(1, 0, 0, 0);
        RowDefinitions.Add(new() { Height = new GridLength(29) });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var tabs = new Grid { Background = ClassicChrome.Background, BorderBrush = ClassicChrome.Border, BorderThickness = new(0, 0, 0, 1) };
        tabs.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        tabs.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Margin = new(2, 0, 3, 0) };
        foreach (var (tab, icon) in new[] { ("Properties", "Properties"), ("Layers", "Layer"), ("Objects", "Objects") })
        {
            var button = ClassicChrome.IconButton(tab, icon, () => ShowTab(tab), "Panel" + tab);
            _tabButtons[tab] = button; icons.Children.Add(button);
        }
        tabs.Children.Add(icons); _heading.Margin = new(5, 0, 7, 0); _heading.TextTrimming = TextTrimming.CharacterEllipsis;
        tabs.Children.Add(_heading); SetColumn(_heading, 1); Children.Add(tabs);
        AutomationProperties.SetAutomationId(_heading, "InspectorHeading");
        BuildLayersPanel(); BuildObjectsPanel();
        _propertyScroll = new()
        {
            Content = _properties, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = ClassicChrome.PanelBackground,
        };
        foreach (var element in new FrameworkElement[] { _layerPanel, _objectPanel, _propertyScroll })
        { Children.Add(element); SetRow(element, 1); }
        AutomationProperties.SetAutomationId(_propertyScroll, "ObjectPropertiesScroll");
        _objects.SelectionChanged += (_, _) =>
        {
            if (!_updating && _document is not null)
                _document.Select(_objects.SelectedItems.OfType<InspectorObjectRow>().Select(o => o.Id));
        };
        ShowTab("Layers");
    }

    public void ShowTab(string tab)
    {
        if (tab is not ("Properties" or "Layers" or "Objects")) throw new ArgumentException("Unknown inspector tab.");
        var changed = ActiveTab != tab; ActiveTab = tab; _heading.Text = tab;
        _layerPanel.Visibility = tab == "Layers" ? Visibility.Visible : Visibility.Collapsed;
        _objectPanel.Visibility = tab == "Objects" ? Visibility.Visible : Visibility.Collapsed;
        _propertyScroll.Visibility = tab == "Properties" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (name, button) in _tabButtons) ClassicChrome.SelectTab(button, name == tab);
        if (changed) TabChanged?.Invoke(tab);
    }
    private void OnChanged(DocumentChange change) => Refresh();
    private void Change(string name, Action<SceneDocument> action)
    {
        if (_document is null || _updating) return;
        try { _document.Transaction(name, () => action(_document)); }
        catch (Exception error) { Message?.Invoke(error.Message); }
    }
    private void ChangeLayer(SceneDocument owner, string id, string name, Action<SceneDocument, Layer> action)
    {
        if (!ReferenceEquals(owner, _document) || !owner.Layers.Any(l => l.Id == id)) return;
        Change(name, document => action(document, document.GetLayer(id)));
    }
    private void Refresh()
    {
        if (_updating) return; _updating = true;
        try
        {
            if (_document is null)
            {
                _rows.Clear(); _visibleRows.Clear(); _objects.ItemsSource = null; _properties.Children.Clear();
                _layerCount.Text = "0 layers"; _objectCount.Text = "0 objects"; UpdateLayerSelection(true); return;
            }
            var owner = _document; var layers = owner.Layers;
            if (!ReferenceEquals(_layerSnapshot, layers) && (_layerSnapshot is null || !_layerSnapshot.SequenceEqual(layers))) _layerVersion++;
            _layerSnapshot = layers;
            if (_rows.Count != layers.Count || !_rows.Select(r => r.Id).SequenceEqual(layers.Select(l => l.Id)))
            {
                var retained = _rows.ToDictionary(r => r.Id); _rows.Clear();
                foreach (var layer in layers)
                {
                    if (!retained.TryGetValue(layer.Id, out var row))
                        row = new(layer,
                            (id, visible, locked) => ChangeLayer(owner, id, "Layer state", (d, l) => d.UpdateLayer(l with { Visible = visible, Locked = locked })),
                            id => ChangeLayer(owner, id, "Current layer", (d, _) => d.ActiveLayer = id),
                            id => _ = EditLayerColorAsync(owner, id));
                    _rows.Add(row);
                }
                LayerStructureChanges++;
            }
            for (var i = 0; i < layers.Count; i++) _rows[i].Apply(layers[i], layers[i].Id == owner.ActiveLayer);
            ApplyLayerFilter();
            var objects = owner.Objects;
            var objectStructureChanged = _objects.ItemsSource is not IReadOnlyList<InspectorObjectRow> shown
                || shown.Count != objects.Count || !shown.Select(r => r.Id).SequenceEqual(objects.Select(o => o.Id));
            if (objectStructureChanged)
            {
                var old = new Dictionary<string, InspectorObjectRow>(_objectRows); _objectRows.Clear();
                var rows = new InspectorObjectRow[objects.Count];
                for (var i = 0; i < objects.Count; i++)
                {
                    var value = objects[i];
                    if (!old.TryGetValue(value.Id, out var row)) row = new(value, owner.GetLayer(value.LayerId));
                    else row.Apply(value, owner.GetLayer(value.LayerId));
                    _objectRows.Add(value.Id, row); rows[i] = row;
                }
                _objects.ItemsSource = rows;
            }
            if (!ReferenceEquals(_objectSnapshot, objects))
                foreach (var value in objects) if (_objectRows.TryGetValue(value.Id, out var row)) row.Apply(value, owner.GetLayer(value.LayerId));
            _objectSnapshot = objects;
            var selected = objects.Where(o => owner.Selection.Contains(o.Id)).ToArray();
            if (!_objects.SelectedItems.OfType<InspectorObjectRow>().Select(o => o.Id).ToHashSet().SetEquals(owner.Selection))
            {
                _objects.SelectedItems.Clear(); foreach (var value in selected) _objects.SelectedItems.Add(_objectRows[value.Id]);
            }
            _objectCount.Text = $"{objects.Count} objects  ·  {selected.Length} selected";
            RefreshProperties(owner, objects, selected);
        }
        finally { _updating = false; }
    }
    public void Detach()
    {
        _colorDialog?.Hide(); if (_document is not null) _document.Changed -= OnChanged; _document = null;
    }
}
