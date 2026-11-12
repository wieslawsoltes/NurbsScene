using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using NurbsScene.Geometry;
using NurbsScene.Model;

namespace NurbsScene.Controls;

public sealed partial class LayerInspector
{
    private void BuildObjectsPanel()
    {
        _objectPanel.RowDefinitions.Add(new() { Height = new GridLength(31) });
        _objectPanel.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _objectPanel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(3, 1, 3, 0), Spacing = 1 };
        toolbar.Children.Add(ClassicChrome.IconButton("Select all editable objects", "Select", () =>
        { if (_document is { } owner) owner.Select(owner.Objects.Select(o => o.Id)); }, "SelectAllObjects"));
        toolbar.Children.Add(ClassicChrome.IconButton("Clear object selection", "SelectNone", () => _document?.Select([]), "ClearObjectSelection"));
        toolbar.Children.Add(ClassicChrome.Separator());
        toolbar.Children.Add(ClassicChrome.IconButton("Edit selected object properties", "Properties", () => ShowTab("Properties"), "ShowObjectProperties"));
        _objectPanel.Children.Add(toolbar);
        _objects.ItemContainerStyle = DenseItemStyle(43);
        _objects.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Grid MinHeight="42" BorderBrush="#EBEBEB" BorderThickness="0,0,0,1" Padding="4,4,3,4" ColumnSpacing="7">
                <Grid.ColumnDefinitions><ColumnDefinition Width="15"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                <Border Width="13" Height="13" Background="{Binding Color}" BorderBrush="#888888" BorderThickness="1" VerticalAlignment="Top" Margin="0,3,0,0"/>
                <StackPanel Grid.Column="1" Spacing="2">
                  <TextBlock Text="{Binding Name}" FontSize="11" TextTrimming="CharacterEllipsis" ToolTipService.ToolTip="{Binding Name}"/>
                  <TextBlock Text="{Binding Description}" FontSize="10" Foreground="#62686B" TextTrimming="CharacterEllipsis" ToolTipService.ToolTip="{Binding Description}"/>
                </StackPanel>
              </Grid>
            </DataTemplate>
            """);
        _objects.Background = ClassicChrome.Surface; _objects.Padding = new(3, 0, 3, 0);
        AutomationProperties.SetAutomationId(_objects, "ObjectList");
        _objectPanel.Children.Add(_objects); SetRow(_objects, 1);
        _objectCount.Margin = new(7, 6, 7, 6); _objectCount.Foreground = ClassicChrome.MutedInk;
        _objectPanel.Children.Add(_objectCount); SetRow(_objectCount, 2);
    }
    private void RefreshProperties(SceneDocument owner, IReadOnlyList<SceneObject> objects, SceneObject[] selected)
    {
        var key = $"{owner.Name}:{objects.Count}:{owner.Units}:{owner.AbsoluteTolerance}:{_layerVersion}";
        if (key == _propertyKey && _propertySelection.SequenceEqual(selected)) return;
        _propertyKey = key; _propertySelection = selected; _properties.Children.Clear();
        if (selected.Length == 0 && _emptySelectionContent is not null)
        { _properties.Children.Add(_emptySelectionContent); return; }
        if (selected.Length == 0)
        {
            _properties.Children.Add(ClassicChrome.SectionHeader("Document"));
            _properties.Children.Add(InspectorLayout.Value("Name", owner.Name));
            _properties.Children.Add(InspectorLayout.Value("Units", owner.Units));
            _properties.Children.Add(InspectorLayout.Value("Tolerance", owner.AbsoluteTolerance.ToString("G6", CultureInfo.InvariantCulture)));
            _properties.Children.Add(InspectorLayout.Value("Objects", objects.Count.ToString(CultureInfo.InvariantCulture)));
            return;
        }
        _properties.Children.Add(ClassicChrome.SectionHeader(selected.Length == 1 ? "Object" : "Selection"));
        if (selected.Length == 1)
        {
            var value = selected[0];
            _properties.Children.Add(InspectorLayout.Value("Type", InspectorObjectRow.Kind(value.Geometry)));
            var name = InspectorLayout.Editor(value.Name, "Object name", "ObjectName");
            InspectorScrolling.Attach(name, _propertyScroll);
            name.LostFocus += (_, _) =>
            {
                if (name.IsLoaded && !_updating && ReferenceEquals(owner, _document) && name.Text != value.Name && owner.Objects.Any(o => o.Id == value.Id))
                    Change("Rename object", d => d.Update(d.GetObject(value.Id) with { Name = name.Text }));
            };
            _properties.Children.Add(InspectorLayout.Row("Name", name));
            AddObjectLayerChoice(owner, selected);
            var colorRow = new Grid { ColumnSpacing = 3 };
            colorRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            colorRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var effective = owner.ObjectColor(value);
            var color = ClassicChrome.Button(InspectorLayout.Hex(effective), () => _ = EditObjectColorAsync(owner, value), "ObjectDisplayColor");
            var colorContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            colorContent.Children.Add(InspectorLayout.Swatch(effective, 13)); colorContent.Children.Add(ClassicChrome.Label(value.Color is null ? "By layer" : InspectorLayout.Hex(effective), 10));
            color.Content = colorContent; color.HorizontalAlignment = HorizontalAlignment.Stretch; color.Padding = new(4, 2, 4, 2);
            colorRow.Children.Add(color);
            var inherit = ClassicChrome.IconButton("Use layer display color", "Layer", () =>
            {
                if (ReferenceEquals(owner, _document) && owner.Objects.Any(o => o.Id == value.Id) && owner.GetObject(value.Id).Color is not null)
                    Change("Object color by layer", d => d.Update(d.GetObject(value.Id) with { Color = null }));
            }, "ObjectColorByLayer");
            inherit.IsEnabled = value.Color is not null; colorRow.Children.Add(inherit); SetColumn(inherit, 1);
            _properties.Children.Add(InspectorLayout.Row("Display color", colorRow));
            _properties.Children.Add(ClassicChrome.SectionHeader("Geometry"));
            switch (value.Geometry)
            {
                case NurbsCurve curve:
                    _properties.Children.Add(InspectorLayout.Value("Degree", curve.Degree.ToString(CultureInfo.InvariantCulture)));
                    _properties.Children.Add(InspectorLayout.Value("Control points", curve.Count.ToString(CultureInfo.InvariantCulture)));
                    _properties.Children.Add(InspectorLayout.Value("Domain", FormattableString.Invariant($"{curve.Domain.Min:G6} … {curve.Domain.Max:G6}")));
                    break;
                case NurbsSurface surface:
                    _properties.Children.Add(InspectorLayout.Value("Degree U / V", $"{surface.DegreeU} / {surface.DegreeV}"));
                    _properties.Children.Add(InspectorLayout.Value("Control net", $"{surface.CountU} × {surface.CountV}"));
                    break;
            }
            if (value.Source is { Length: > 0 } source) _properties.Children.Add(InspectorLayout.Value("Source", source));
            AddBounds(value.Geometry.Bounds, owner.Units);
        }
        else
        {
            _properties.Children.Add(InspectorLayout.Value("Objects", selected.Length.ToString(CultureInfo.InvariantCulture)));
            _properties.Children.Add(InspectorLayout.Value("Curves", selected.Count(o => o.Geometry is NurbsCurve).ToString(CultureInfo.InvariantCulture)));
            _properties.Children.Add(InspectorLayout.Value("Surfaces", selected.Count(o => o.Geometry is NurbsSurface).ToString(CultureInfo.InvariantCulture)));
            AddObjectLayerChoice(owner, selected);
            var bounds = Bounds3.Empty; foreach (var value in selected) bounds = bounds.Union(value.Geometry.Bounds);
            AddBounds(bounds, owner.Units);
        }
    }
    private void AddBounds(Bounds3 bounds, string units)
    {
        _properties.Children.Add(ClassicChrome.SectionHeader("Control-hull bounds"));
        var size = bounds.Size;
        foreach (var (label, value) in new[] { ("X size", size.X), ("Y size", size.Y), ("Z size", size.Z) })
            _properties.Children.Add(InspectorLayout.Value(label, value.ToString("G8", CultureInfo.InvariantCulture) + " " + units));
    }
    private void AddObjectLayerChoice(SceneDocument owner, SceneObject[] selected)
    {
        var common = selected.Select(o => o.LayerId).Distinct().Take(2).ToArray();
        var choice = new ComboBox
        {
            ItemsSource = owner.Layers, DisplayMemberPath = "Name", SelectedItem = common.Length == 1 ? owner.GetLayer(common[0]) : null,
            PlaceholderText = "Varies", HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 11, MinWidth = 0, MinHeight = 26, Padding = new(5, 2, 20, 2),
        };
        AutomationProperties.SetAutomationId(choice, "ObjectLayer"); AutomationProperties.SetName(choice, "Object layer");
        choice.SelectionChanged += (_, _) =>
        {
            if (_updating || !choice.IsLoaded || !ReferenceEquals(owner, _document) || choice.SelectedItem is not Layer layer
                || !owner.Selection.SetEquals(selected.Select(o => o.Id))) return;
            if (!owner.Layers.Any(l => l.Id == layer.Id)) return;
            layer = owner.GetLayer(layer.Id);
            var values = selected.Select(o => owner.GetObject(o.Id)).ToArray();
            if (values.All(o => o.LayerId == layer.Id)) return;
            if (layer.Locked || values.Any(o => !owner.CanEdit(o))) { Message?.Invoke("Select editable objects and an unlocked target layer."); return; }
            Change("Object layer", d => { foreach (var value in values) d.Update(value with { LayerId = layer.Id }); });
        };
        _properties.Children.Add(InspectorLayout.Row("Layer", choice));
    }
    private async Task EditObjectColorAsync(SceneDocument owner, SceneObject source)
    {
        if (_colorDialog is not null || XamlRoot is null || !ReferenceEquals(owner, _document) || !owner.Objects.Any(o => o.Id == source.Id)) return;
        var initialColor = owner.ObjectColor(source);
        var dialog = new InspectorColorDialog("Object display color", initialColor) { XamlRoot = XamlRoot };
        _colorDialog = dialog;
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.SelectedColor is not { } color) return;
            if (!ReferenceEquals(owner, _document) || !owner.Objects.Any(o => o.Id == source.Id) || owner.GetObject(source.Id) != source
                || owner.ObjectColor(source) != initialColor)
                throw new InvalidOperationException("The object changed while its color was being edited. Reopen the color editor.");
            if (color != initialColor) Change("Object display color", d => d.Update(source with { Color = color }));
        }
        catch (Exception error) { Message?.Invoke(error.Message); }
        finally { if (ReferenceEquals(_colorDialog, dialog)) _colorDialog = null; }
    }
}

[Microsoft.UI.Xaml.Data.Bindable]
public sealed class InspectorObjectRow : INotifyPropertyChanged
{
    private SceneObject _value;
    private Layer _layer;
    private SolidColorBrush _color;
    public InspectorObjectRow(SceneObject value, Layer layer) { _value = value; _layer = layer; _color = InspectorLayout.ColorBrush(value.Color ?? layer.Color); }
    public string Id => _value.Id;
    public string Name => string.IsNullOrWhiteSpace(_value.Name) ? "Unnamed " + Kind(_value.Geometry).ToLowerInvariant() : _value.Name;
    public string Description => Kind(_value.Geometry) + " · " + _layer.Name
        + (!_value.Visible || !_layer.Visible ? " · hidden" : _value.Locked || _layer.Locked ? " · locked" : "");
    public SolidColorBrush Color => _color;
    public event PropertyChangedEventHandler? PropertyChanged;
    public static string Kind(IGeometry geometry) => geometry switch { NurbsCurve => "NURBS curve", NurbsSurface => "NURBS surface", _ => geometry.GetType().Name };
    public void Apply(SceneObject value, Layer layer)
    {
        if (_value == value && _layer == layer) return;
        var oldColor = _value.Color ?? _layer.Color; var color = value.Color ?? layer.Color;
        _value = value; _layer = layer;
        if (oldColor != color) _color = InspectorLayout.ColorBrush(color);
        PropertyChanged?.Invoke(this, new(null));
    }
}
