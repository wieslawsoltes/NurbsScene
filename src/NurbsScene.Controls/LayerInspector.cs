using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using NurbsScene.Geometry;
using NurbsScene.Model;

namespace NurbsScene.Controls;

/// <summary>Tabbed object/layer/property panel. Layer rows retain identity across
/// selection changes; virtualized lists replace the former rebuilt control tree.</summary>
public sealed class LayerInspector : Grid
{
    private readonly ObservableCollection<LayerRow> _rows=[];
    private readonly ListView _layers=new(){SelectionMode=ListViewSelectionMode.Single,MinHeight=60};
    private readonly ListView _objects=new(){SelectionMode=ListViewSelectionMode.Multiple,DisplayMemberPath="Name",MinHeight=100};
    private readonly StackPanel _properties=new(){Spacing=6,Padding=new(10)};
    private readonly Grid _layerPanel=new();
    private readonly ScrollViewer _propertyScroll;
    private readonly TextBox _layerName=new(){PlaceholderText="Layer name",FontSize=11,MinHeight=24,Padding=new(5,2,5,2)};
    private readonly StackPanel _tabs=new(){Orientation=Orientation.Horizontal,Spacing=1};
    private SceneDocument? _document;private bool _updating;
    private IReadOnlyList<SceneObject>? _objectSnapshot;
    private string _propertyKey="";
    public long LayerStructureChanges {get;private set;}
    public string ActiveTab {get;private set;}="Layers";
    public event Action<string>? Message;
    public event Action<string>? TabChanged;
    private FrameworkElement? _emptySelectionContent;
    public FrameworkElement? EmptySelectionContent{get=>_emptySelectionContent;set{if(ReferenceEquals(value,_emptySelectionContent))return;_emptySelectionContent=value;_propertyKey="";Refresh();}}
    public SceneDocument? Document
    {
        get=>_document;
        set{if(ReferenceEquals(value,_document))return;if(_document is not null)_document.Changed-=OnChanged;_document=value;_rows.Clear();_objectSnapshot=null;_propertyKey="";if(value is not null)value.Changed+=OnChanged;Refresh();}
    }
    public LayerInspector()
    {
        Background=ClassicChrome.Background;
        RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        foreach(var tab in new[]{"Properties","Layers","Objects"})_tabs.Children.Add(ClassicChrome.Button(tab,()=>ShowTab(tab),"Panel"+tab));Children.Add(_tabs);
        _layerPanel.RowDefinitions.Add(new(){Height=new GridLength(24)});_layerPanel.RowDefinitions.Add(new(){Height=GridLength.Auto});_layerPanel.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});_layerPanel.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var toolbar=new StackPanel{Orientation=Orientation.Horizontal,Spacing=2};
        toolbar.Children.Add(ClassicChrome.IconButton("New layer","New",()=>Change("New layer",d=>d.AddLayer("Layer "+d.Layers.Count,ColorRgba.Default)),"NewLayer"));
        toolbar.Children.Add(ClassicChrome.IconButton("Delete selected layer","Delete",()=>{if(_layers.SelectedItem is LayerRow row)Change("Delete layer",d=>d.RemoveLayer(row.Id));}));
        toolbar.Children.Add(ClassicChrome.Button("Set current",()=>{if(_layers.SelectedItem is LayerRow row)Change("Current layer",d=>d.ActiveLayer=row.Id);},"SetCurrentLayer"));
        _layerPanel.Children.Add(toolbar);
        var header=new Grid{Margin=new(12,4,12,4)};
        foreach(var width in new[]{new GridLength(16),new GridLength(1,GridUnitType.Star),new GridLength(28),new GridLength(32),new GridLength(24)})header.ColumnDefinitions.Add(new(){Width=width});
        var captions=new[]{"","Name","On","Lock",""};for(var i=0;i<captions.Length;i++){var label=ClassicChrome.Label(captions[i],10);header.Children.Add(label);SetColumn(label,i);}_layerPanel.Children.Add(header);SetRow(header,1);
        _layers.ItemTemplate=(DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Grid Height="28" MinWidth="180">
                <Grid.ColumnDefinitions><ColumnDefinition Width="16"/><ColumnDefinition Width="*"/><ColumnDefinition Width="28"/><ColumnDefinition Width="32"/><ColumnDefinition Width="24"/></Grid.ColumnDefinitions>
                <TextBlock Text="{Binding Current}" VerticalAlignment="Center" FontSize="12"/>
                <TextBlock Grid.Column="1" Text="{Binding Name}" FontSize="11" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
                <CheckBox Grid.Column="2" IsChecked="{Binding Visible, Mode=TwoWay}" MinWidth="0" MinHeight="0" Width="24" Height="24" Padding="0" ToolTipService.ToolTip="Layer visibility"/>
                <CheckBox Grid.Column="3" IsChecked="{Binding Locked, Mode=TwoWay}" MinWidth="0" MinHeight="0" Width="24" Height="24" Padding="0" ToolTipService.ToolTip="Lock layer"/>
                <Border Grid.Column="4" Width="14" Height="14" Background="{Binding Color}" BorderThickness="1" BorderBrush="#777777" VerticalAlignment="Center"/>
              </Grid>
            </DataTemplate>
            """);
        _layers.ItemsSource=_rows;_layers.Padding=new(0);_layers.SelectionChanged+=(_,_)=>{if(_layers.SelectedItem is LayerRow row)_layerName.Text=row.Name;};_layerPanel.Children.Add(_layers);SetRow(_layers,2);
        var naming=new StackPanel{Spacing=4,Margin=new(6)};naming.Children.Add(_layerName);
        naming.Children.Add(ClassicChrome.Button("Rename selected layer",()=>{if(_layers.SelectedItem is LayerRow row&&_layerName.Text.Trim().Length>0)Change("Rename layer",d=>d.UpdateLayer(d.GetLayer(row.Id) with{Name=_layerName.Text.Trim()}));}));
        _layerPanel.Children.Add(naming);SetRow(naming,3);
        _propertyScroll=new(){Content=_properties,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        foreach(var element in new UIElement[]{_layerPanel,_objects,_propertyScroll}){Children.Add(element);SetRow((FrameworkElement)element,1);}
        _objects.SelectionChanged+=(_,_)=>{if(!_updating&&_document is not null)_document.Select(_objects.SelectedItems.OfType<SceneObject>().Select(o=>o.Id));};ShowTab("Layers");
    }
    public void ShowTab(string tab)
    {
        if(tab is not("Properties" or "Layers" or "Objects"))throw new ArgumentException("Unknown inspector tab.");
        var changed=ActiveTab!=tab;ActiveTab=tab;_layerPanel.Visibility=tab=="Layers"?Visibility.Visible:Visibility.Collapsed;_objects.Visibility=tab=="Objects"?Visibility.Visible:Visibility.Collapsed;_propertyScroll.Visibility=tab=="Properties"?Visibility.Visible:Visibility.Collapsed;
        foreach(var b in _tabs.Children.OfType<Button>())b.Background=b.Content?.ToString()==tab?ClassicChrome.Brush(255,255,255):ClassicChrome.Background;
        if(changed)TabChanged?.Invoke(tab);
    }
    private void OnChanged(DocumentChange change)=>Refresh();
    private void Change(string name,Action<SceneDocument> action){if(_document is null||_updating)return;try{_document.Transaction(name,()=>action(_document));}catch(Exception error){Message?.Invoke(error.Message);}}
    private void Refresh()
    {
        if(_updating)return;_updating=true;
        try
        {
            if(_document is null){_rows.Clear();_objects.ItemsSource=null;_properties.Children.Clear();return;}
            var layers=_document.Layers;
            // Reconcile a small layer list only on a model change; selection-only
            // events update flags in place and never recreate row controls.
            if(_rows.Count!=layers.Count||!_rows.Select(r=>r.Id).SequenceEqual(layers.Select(l=>l.Id)))
            {
                var selected=(_layers.SelectedItem as LayerRow)?.Id;_rows.Clear();
                foreach(var layer in layers)_rows.Add(new(layer,(id,visible,locked)=>Change("Layer state",d=>d.UpdateLayer(d.GetLayer(id) with{Visible=visible,Locked=locked}))));
                _layers.SelectedItem=_rows.FirstOrDefault(r=>r.Id==(selected??_document.ActiveLayer));LayerStructureChanges++;
            }
            for(var i=0;i<layers.Count;i++)_rows[i].Apply(layers[i],layers[i].Id==_document.ActiveLayer);
            var objects=_document.Objects;
            if(_objectSnapshot is null||!_objectSnapshot.SequenceEqual(objects)){_objects.ItemsSource=objects;_objectSnapshot=objects;}
            var selectedObjects=objects.Where(o=>_document.Selection.Contains(o.Id)).ToArray();
            if(!_objects.SelectedItems.OfType<SceneObject>().Select(o=>o.Id).ToHashSet().SetEquals(_document.Selection))
            {
                _objects.SelectedItems.Clear();foreach(var o in selectedObjects)_objects.SelectedItems.Add(o);
            }
            var key=selectedObjects.Length==1?selectedObjects[0].ToString():$"{_document.Name}:{objects.Count}:{selectedObjects.Length}:{_document.Units}";
            if(key==_propertyKey)return;_propertyKey=key;_properties.Children.Clear();
            void Label(string text)=>_properties.Children.Add(new TextBlock{Text=text,FontSize=11,TextWrapping=TextWrapping.Wrap,Foreground=ClassicChrome.Ink});
            if(selectedObjects.Length==0&&_emptySelectionContent is not null){_properties.Children.Add(_emptySelectionContent);return;}
            if(selectedObjects.Length!=1){Label(selectedObjects.Length==0?"Document properties":$"{selectedObjects.Length} objects selected");Label($"Units: {_document.Units}");Label($"Absolute tolerance: {_document.AbsoluteTolerance:G}");Label($"Objects: {objects.Count}");return;}
            var value=selectedObjects[0];var owner=_document;
            var name=new TextBox{Header="Object name",Text=value.Name,FontSize=11,MinHeight=25};
            name.LostFocus+=(_,_)=>{if(!_updating&&ReferenceEquals(owner,_document)&&name.Text!=value.Name&&owner.Objects.Any(o=>o.Id==value.Id))Change("Rename object",d=>d.Update(d.GetObject(value.Id) with{Name=name.Text}));};_properties.Children.Add(name);
            var layerChoice=new ComboBox{Header="Layer",ItemsSource=layers,DisplayMemberPath="Name",SelectedItem=_document.GetLayer(value.LayerId),HorizontalAlignment=HorizontalAlignment.Stretch,FontSize=11};
            layerChoice.SelectionChanged+=(_,_)=>{if(!_updating&&ReferenceEquals(owner,_document)&&layerChoice.SelectedItem is Layer layer&&layer.Id!=value.LayerId&&owner.Objects.Any(o=>o.Id==value.Id))Change("Object layer",d=>d.Update(d.GetObject(value.Id) with{LayerId=layer.Id}));};_properties.Children.Add(layerChoice);
            Label(value.Geometry switch{NurbsCurve c=>$"NURBS curve\nDegree: {c.Degree}\nControls: {c.Count}\nDomain: [{c.Domain.Min:G}, {c.Domain.Max:G}]",NurbsSurface s=>$"NURBS surface\nDegree: {s.DegreeU} × {s.DegreeV}\nControls: {s.CountU} × {s.CountV}",_=>value.Geometry.GetType().Name});
            var size=value.Geometry.Bounds.Size;Label($"Control-hull bounds\nX: {size.X:G6}\nY: {size.Y:G6}\nZ: {size.Z:G6}");
        }
        finally{_updating=false;}
    }
    public void Detach(){if(_document is not null)_document.Changed-=OnChanged;_document=null;}
}
[Microsoft.UI.Xaml.Data.Bindable]
public sealed class LayerRow : INotifyPropertyChanged
{
    private Layer _layer;private readonly Action<string,bool,bool> _changed;
    public LayerRow(Layer layer,Action<string,bool,bool> changed){_layer=layer;_changed=changed;}
    public string Id=>_layer.Id;public string Name=>_layer.Name;public string Current{get;private set;}="";
    public bool Visible{get=>_layer.Visible;set{if(value!=Visible)_changed(Id,value,Locked);}}
    public bool Locked{get=>_layer.Locked;set{if(value!=Locked)_changed(Id,Visible,value);}}
    public SolidColorBrush Color=>ClassicChrome.Brush((byte)(_layer.Color.R*255),(byte)(_layer.Color.G*255),(byte)(_layer.Color.B*255));
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Apply(Layer layer,bool current)
    {
        var mark=current?"•":"";if(_layer==layer&&Current==mark)return;_layer=layer;Current=mark;
        PropertyChanged?.Invoke(this,new(null));
    }
}
