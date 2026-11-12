using System.ComponentModel;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using NurbsScene.Model;
using Windows.System;

namespace NurbsScene.Controls;

public sealed partial class LayerInspector
{
    private void BuildLayersPanel()
    {
        ConfigureLayerToggleResources();
        foreach (var height in new[] { new GridLength(30), new GridLength(30), new GridLength(23), new GridLength(1, GridUnitType.Star), GridLength.Auto })
            _layerPanel.RowDefinitions.Add(new() { Height = height });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Margin = new(3, 1, 3, 0) };
        toolbar.Children.Add(ClassicChrome.IconButton("New layer · Insert", "New", NewLayer, "NewLayer"));
        _deleteLayer = ClassicChrome.IconButton("Delete selected layer · Delete", "Delete", DeleteLayer, "DeleteLayer");
        _currentLayer = ClassicChrome.IconButton("Set current layer", "Check", SetCurrentLayer, "SetCurrentLayer");
        _selectLayerObjects = ClassicChrome.IconButton("Select objects on this layer", "Select", SelectLayerObjects, "SelectLayerObjects");
        _colorLayer = ClassicChrome.IconButton("Edit layer display color", "Color", () =>
        { if (_document is { } owner && SelectedLayerId is { } id) _ = EditLayerColorAsync(owner, id); }, "EditLayerColor");
        toolbar.Children.Add(_deleteLayer); toolbar.Children.Add(ClassicChrome.Separator());
        toolbar.Children.Add(_currentLayer); toolbar.Children.Add(_selectLayerObjects); toolbar.Children.Add(_colorLayer);
        _layerPanel.Children.Add(toolbar);
        var filter = new Grid { Margin = new(6, 1, 6, 2), ColumnSpacing = 4 };
        filter.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        filter.ColumnDefinitions.Add(new() { Width = new GridLength(25) });
        _layerFilter.PlaceholderText = "Filter layers…"; _layerFilter.MinHeight = 25; filter.Children.Add(_layerFilter);
        var clear = ClassicChrome.IconButton("Clear layer filter", "Close", () => _layerFilter.Text = "", "ClearLayerFilter");
        clear.Width = 25; clear.Height = 25; filter.Children.Add(clear); SetColumn(clear, 1);
        _layerFilter.TextChanged += (_, _) => { if (!_updating) ApplyLayerFilter(); };
        _layerPanel.Children.Add(filter); SetRow(filter, 1);
        var header = new Grid { Padding = new(5, 0, 5, 0), Background = ClassicChrome.Background, BorderBrush = ClassicChrome.Border, BorderThickness = new(0, 1, 0, 1) };
        foreach (var width in new[] { new GridLength(20), new GridLength(1, GridUnitType.Star), new GridLength(26), new GridLength(26), new GridLength(27) })
            header.ColumnDefinitions.Add(new() { Width = width });
        var labels = new[] { "", "Name", "On", "Lock", "Color" };
        for (var i = 0; i < labels.Length; i++)
        {
            var label = ClassicChrome.Label(labels[i], 10); label.Foreground = ClassicChrome.MutedInk;
            if (i > 1) label.HorizontalAlignment = HorizontalAlignment.Center;
            header.Children.Add(label); SetColumn(label, i);
        }
        _layerPanel.Children.Add(header); SetRow(header, 2);
        _layers.ItemContainerStyle = DenseItemStyle(27);
        _layers.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Grid Height="27" MinWidth="190" BorderThickness="0,0,0,1" BorderBrush="#ECECEC">
                <Grid.ColumnDefinitions><ColumnDefinition Width="20"/><ColumnDefinition Width="*"/><ColumnDefinition Width="26"/><ColumnDefinition Width="26"/><ColumnDefinition Width="27"/></Grid.ColumnDefinitions>
                <Button Command="{Binding MakeCurrentCommand}" AutomationProperties.AutomationId="{Binding CurrentAutomationId}" AutomationProperties.Name="{Binding CurrentLabel}" ToolTipService.ToolTip="Set current layer" Width="20" Height="25" MinWidth="0" MinHeight="0" Padding="2" Background="Transparent" BorderThickness="0" CornerRadius="0">
                  <Path Data="M 1,7 L 5,11 L 13,2" Stroke="#394D33" StrokeThickness="1.8" Width="14" Height="13" Opacity="{Binding CurrentOpacity}"/>
                </Button>
                <TextBlock Grid.Column="1" Text="{Binding Name}" FontSize="11" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" ToolTipService.ToolTip="{Binding Name}" Margin="2,0,2,0"/>
                <ToggleButton Grid.Column="2" IsChecked="{Binding Visible, Mode=TwoWay}" AutomationProperties.AutomationId="{Binding VisibilityAutomationId}" AutomationProperties.Name="{Binding VisibilityLabel}" ToolTipService.ToolTip="Layer visibility" Width="25" Height="25" MinWidth="0" MinHeight="0" Padding="5,3" Background="Transparent" BorderThickness="0" CornerRadius="0">
                  <Viewbox Width="13" Height="17"><Canvas Width="13" Height="17"><Path Data="M 6,1 C 1,1 0,5 3,9 L 4,11 L 8,11 L 9,9 C 12,5 11,1 6,1 Z" Fill="{Binding VisibilityBrush}" Stroke="#666666" StrokeThickness="1"/><Path Data="M 4,13 L 8,13 M 5,15 L 7,15" Stroke="#666666" StrokeThickness="1.2"/><Path Data="M 1,1 L 12,15" Stroke="#A85C4D" StrokeThickness="1.4" Visibility="{Binding HiddenMarkVisibility}"/></Canvas></Viewbox>
                </ToggleButton>
                <ToggleButton Grid.Column="3" IsChecked="{Binding Locked, Mode=TwoWay}" AutomationProperties.AutomationId="{Binding LockAutomationId}" AutomationProperties.Name="{Binding LockLabel}" ToolTipService.ToolTip="Lock layer" Width="25" Height="25" MinWidth="0" MinHeight="0" Padding="5,3" Background="Transparent" BorderThickness="0" CornerRadius="0">
                  <Viewbox Width="14" Height="17"><Canvas Width="14" Height="17"><Path Data="M 3,8 L 3,4 C 3,0 11,0 11,4 L 11,8" Stroke="#676767" StrokeThickness="1.5" Visibility="{Binding LockedVisibility}"/><Path Data="M 7,8 L 7,3 C 7,0 13,0 13,3 L 13,5" Stroke="#8B8B8B" StrokeThickness="1.5" Visibility="{Binding UnlockedVisibility}"/><Rectangle Canvas.Left="1" Canvas.Top="8" Width="12" Height="8" RadiusX="1" RadiusY="1" Fill="{Binding LockBrush}" Stroke="#727272" StrokeThickness="1"/><Path Data="M 7,10 L 7,13" Stroke="#666666" StrokeThickness="1.5"/></Canvas></Viewbox>
                </ToggleButton>
                <Button Grid.Column="4" Command="{Binding EditColorCommand}" AutomationProperties.AutomationId="{Binding ColorAutomationId}" AutomationProperties.Name="{Binding ColorLabel}" ToolTipService.ToolTip="{Binding ColorLabel}" Width="26" Height="25" MinWidth="0" MinHeight="0" Padding="5" Background="Transparent" BorderThickness="0" CornerRadius="0">
                  <Border Width="15" Height="15" Background="{Binding Color}" BorderThickness="1" BorderBrush="#777777"/>
                </Button>
              </Grid>
            </DataTemplate>
            """);
        _layers.ItemsSource = _visibleRows; _layers.Padding = new(3, 0, 3, 0); _layers.Background = ClassicChrome.Surface;
        AutomationProperties.SetAutomationId(_layers, "LayerList");
        _layers.SelectionChanged += (_, _) => { if (!_filtering) UpdateLayerSelection(true); };
        _layers.DoubleTapped += (_, e) =>
        {
            for (var source = e.OriginalSource as DependencyObject; source is not null && !ReferenceEquals(source, _layers); source = VisualTreeHelper.GetParent(source))
                if (source is ButtonBase) return;
            SetCurrentLayer(); e.Handled = true;
        };
        _layers.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.F2 || e.Key == VirtualKey.Enter) { FocusLayerName(); e.Handled = true; }
            else if (e.Key == VirtualKey.Insert) { NewLayer(); e.Handled = true; }
            else if (e.Key == VirtualKey.Delete) { DeleteLayer(); e.Handled = true; }
        };
        _layerPanel.Children.Add(_layers); SetRow(_layers, 3);
        var footer = new StackPanel { Spacing = 4, Padding = new(6, 5, 6, 5), BorderBrush = ClassicChrome.Border, BorderThickness = new(0, 1, 0, 0) };
        var naming = new Grid { ColumnSpacing = 3 };
        naming.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); naming.ColumnDefinitions.Add(new() { Width = new GridLength(27) });
        _layerName.PlaceholderText = "Layer name"; naming.Children.Add(_layerName);
        _renameLayer = ClassicChrome.IconButton("Rename selected layer · Enter", "Check", RenameLayer, "RenameLayer");
        naming.Children.Add(_renameLayer); SetColumn(_renameLayer, 1);
        _layerName.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter) { RenameLayer(); e.Handled = true; }
            else if (e.Key == VirtualKey.Escape) { UpdateLayerSelection(true); e.Handled = true; }
        };
        footer.Children.Add(naming); _layerCount.Foreground = ClassicChrome.MutedInk; footer.Children.Add(_layerCount);
        _layerPanel.Children.Add(footer); SetRow(footer, 4);
    }
    private void ConfigureLayerToggleResources()
    {
        // Uno.WinUI 7.0.32's native ToggleButton template consumes these state
        // resources. Keep its keyboard/focus/capture behavior and let the bulb
        // and padlock glyphs communicate state, without accent-colored cells.
        // Scope the palette to the layer list; other workspace toggles retain
        // their own theme. Transparent resting cells also retain row selection.
        var resources = _layers.Resources;
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var state in new[] { "", "Checked" })
        {
            resources["ToggleButtonBackground" + state] = transparent;
            resources["ToggleButtonBackground" + state + "PointerOver"] = ClassicChrome.PanelBackground;
            resources["ToggleButtonBackground" + state + "Pressed"] = ClassicChrome.Background;
            resources["ToggleButtonBackground" + state + "Disabled"] = transparent;
            resources["ToggleButtonForeground" + state] = ClassicChrome.Ink;
            resources["ToggleButtonForeground" + state + "PointerOver"] = ClassicChrome.Ink;
            resources["ToggleButtonForeground" + state + "Pressed"] = ClassicChrome.Ink;
            resources["ToggleButtonForeground" + state + "Disabled"] = ClassicChrome.MutedInk;
            resources["ToggleButtonBorderBrush" + state] = transparent;
            resources["ToggleButtonBorderBrush" + state + "PointerOver"] = ClassicChrome.Border;
            resources["ToggleButtonBorderBrush" + state + "Pressed"] = ClassicChrome.Border;
            resources["ToggleButtonBorderBrush" + state + "Disabled"] = transparent;
        }
    }
    private static Style DenseItemStyle(double height)
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.MinHeightProperty, height));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 0, 2, 0)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        return style;
    }
    private void ApplyLayerFilter()
    {
        if (_filtering) return; _filtering = true;
        try
        {
            var filter = _layerFilter.Text.Trim();
            var desired = _rows.Where(r => filter.Length == 0 || r.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (!_visibleRows.SequenceEqual(desired))
            {
                _visibleRows.Clear(); foreach (var row in desired) _visibleRows.Add(row);
            }
            var selected = desired.FirstOrDefault(r => r.Id == _selectedLayerId)
                ?? desired.FirstOrDefault(r => r.Id == _document?.ActiveLayer) ?? desired.FirstOrDefault();
            if (!ReferenceEquals(_layers.SelectedItem, selected)) _layers.SelectedItem = selected;
            _layerCount.Text = filter.Length == 0 ? $"{_rows.Count} layers" : $"{desired.Length} of {_rows.Count} layers";
        }
        finally { _filtering = false; }
        UpdateLayerSelection(false);
    }
    private void UpdateLayerSelection(bool replaceDraft)
    {
        var row = _layers.SelectedItem as LayerRow;
        var selectionChanged = row?.Id != _selectedLayerId;
        _selectedLayerId = row?.Id;
        _layerName.IsEnabled = row is not null;
        if (replaceDraft || selectionChanged || _layerName.FocusState == FocusState.Unfocused) _layerName.Text = row?.Name ?? "";
        if (_deleteLayer is not null) _deleteLayer.IsEnabled = row is not null && row.Id != "default";
        if (_currentLayer is not null) _currentLayer.IsEnabled = row is not null && row.Id != _document?.ActiveLayer;
        if (_renameLayer is not null) _renameLayer.IsEnabled = row is not null;
        if (_colorLayer is not null) _colorLayer.IsEnabled = row is not null;
        if (_selectLayerObjects is not null) _selectLayerObjects.IsEnabled = row is not null;
    }
    private void FocusLayerName()
    {
        if (_layers.SelectedItem is null) return;
        _layerName.Focus(FocusState.Programmatic); _layerName.SelectAll();
    }
    private void NewLayer()
    {
        _layerFilter.Text = "";
        Change("New layer", d => _selectedLayerId = d.AddLayer("Layer " + d.Layers.Count, ColorRgba.Default).Id);
    }
    private void DeleteLayer()
    {
        if (_layers.SelectedItem is LayerRow { Id: not "default" } row) Change("Delete layer", d => d.RemoveLayer(row.Id));
    }
    private void SetCurrentLayer()
    {
        if (_layers.SelectedItem is LayerRow row && row.Id != _document?.ActiveLayer) Change("Current layer", d => d.ActiveLayer = row.Id);
    }
    private void SelectLayerObjects()
    {
        if (_document is { } owner && _layers.SelectedItem is LayerRow row)
            owner.Select(owner.Objects.Where(o => o.LayerId == row.Id).Select(o => o.Id));
    }
    private void RenameLayer()
    {
        if (_layers.SelectedItem is not LayerRow row) return;
        var name = _layerName.Text.Trim();
        if (name.Length == 0) { Message?.Invoke("A layer name cannot be empty."); return; }
        if (name != row.Name) Change("Rename layer", d => d.UpdateLayer(d.GetLayer(row.Id) with { Name = name }));
    }
    private async Task EditLayerColorAsync(SceneDocument owner, string id)
    {
        if (_colorDialog is not null || XamlRoot is null || !ReferenceEquals(owner, _document) || !owner.Layers.Any(l => l.Id == id)) return;
        var source = owner.GetLayer(id);
        _layers.SelectedItem = _visibleRows.FirstOrDefault(r => r.Id == id);
        var dialog = new InspectorColorDialog("Layer color · " + source.Name, source.Color) { XamlRoot = XamlRoot };
        _colorDialog = dialog;
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.SelectedColor is not { } color) return;
            if (!ReferenceEquals(owner, _document) || !owner.Layers.Any(l => l.Id == id) || owner.GetLayer(id) != source)
                throw new InvalidOperationException("The layer changed while its color was being edited. Reopen the color editor.");
            if (color != source.Color) ChangeLayer(owner, id, "Layer color", (d, layer) => d.UpdateLayer(layer with { Color = color }));
        }
        catch (Exception error) { Message?.Invoke(error.Message); }
        finally { if (ReferenceEquals(_colorDialog, dialog)) _colorDialog = null; }
    }
}

[Microsoft.UI.Xaml.Data.Bindable]
public sealed class LayerRow : INotifyPropertyChanged
{
    private Layer _layer;
    private readonly Action<string, bool, bool> _changed;
    private SolidColorBrush _color;
    private static readonly SolidColorBrush Lit = ClassicChrome.Brush(230, 202, 92), Dim = ClassicChrome.Brush(215, 218, 219);
    public LayerRow(Layer layer, Action<string, bool, bool> changed) : this(layer, changed, null, null) { }
    public LayerRow(Layer layer, Action<string, bool, bool> changed, Action<string>? makeCurrent, Action<string>? editColor)
    {
        _layer = layer; _changed = changed; _color = InspectorLayout.ColorBrush(layer.Color);
        MakeCurrentCommand = new InspectorAction(() => makeCurrent?.Invoke(Id)); EditColorCommand = new InspectorAction(() => editColor?.Invoke(Id));
    }
    public string Id => _layer.Id;
    public string Name => _layer.Name;
    public string Current { get; private set; } = "";
    public double CurrentOpacity => Current.Length == 0 ? 0 : 1;
    public bool Visible { get => _layer.Visible; set { if (value != Visible) _changed(Id, value, Locked); } }
    public bool Locked { get => _layer.Locked; set { if (value != Locked) _changed(Id, Visible, value); } }
    public SolidColorBrush Color => _color;
    public SolidColorBrush VisibilityBrush => Visible ? Lit : Dim;
    public SolidColorBrush LockBrush => Locked ? Lit : Dim;
    public Visibility HiddenMarkVisibility => Visible ? Visibility.Collapsed : Visibility.Visible;
    public Visibility LockedVisibility => Locked ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UnlockedVisibility => Locked ? Visibility.Collapsed : Visibility.Visible;
    public string CurrentAutomationId => "LayerCurrent_" + Id;
    public string VisibilityAutomationId => "LayerVisible_" + Id;
    public string LockAutomationId => "LayerLocked_" + Id;
    public string ColorAutomationId => "LayerColor_" + Id;
    public string CurrentLabel => (Current.Length > 0 ? "Current layer: " : "Make current: ") + Name;
    public string VisibilityLabel => "Visibility of " + Name;
    public string LockLabel => "Lock " + Name;
    public string ColorLabel => "Color of " + Name + " · " + InspectorLayout.Hex(_layer.Color);
    public ICommand MakeCurrentCommand { get; }
    public ICommand EditColorCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Apply(Layer layer, bool current)
    {
        var mark = current ? "•" : ""; if (_layer == layer && Current == mark) return;
        if (_layer.Color != layer.Color) _color = InspectorLayout.ColorBrush(layer.Color);
        _layer = layer; Current = mark; PropertyChanged?.Invoke(this, new(null));
    }
}
internal sealed class InspectorAction(Action execute) : ICommand
{
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
