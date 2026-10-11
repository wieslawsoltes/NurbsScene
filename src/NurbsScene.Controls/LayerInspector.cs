using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Geometry;
using NurbsScene.Model;

namespace NurbsScene.Controls;

/// <summary>Reusable layer/object inspector with independent layer visibility and locks.</summary>
public sealed class LayerInspector : Grid
{
    private readonly StackPanel _layers=new(){Spacing=4,Padding=new(8)};
    private readonly ListView _objects=new(){SelectionMode=ListViewSelectionMode.Multiple,DisplayMemberPath="Name",MinHeight=100};
    private readonly StackPanel _properties=new(){Spacing=5,Padding=new(10)};
    private SceneDocument? _document;private bool _updating;
    public event Action<string>? Message;
    public SceneDocument? Document{get=>_document;set{if(_document is not null)_document.Changed-=OnChanged;_document=value;if(value is not null)value.Changed+=OnChanged;Refresh();}}
    public LayerInspector()
    {
        RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});RowDefinitions.Add(new(){Height=GridLength.Auto});
        Children.Add(new ScrollViewer{Content=_layers,MaxHeight=250,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Children.Add(_objects);SetRow(_objects,1);Children.Add(_properties);SetRow(_properties,2);
        _objects.SelectionChanged+=(_,_)=>{if(!_updating&&_document is not null)_document.Select(_objects.SelectedItems.OfType<SceneObject>().Select(o=>o.Id));};
    }
    private void OnChanged(DocumentChange change)=>Refresh();
    private void Change(string name,Action<SceneDocument> action){if(_document is null)return;try{_document.Transaction(name,()=>action(_document));}catch(Exception error){Message?.Invoke(error.Message);}}
    private void Refresh()
    {
        _updating=true;try
        {
            _layers.Children.Clear();_properties.Children.Clear();if(_document is null)return;
            _layers.Children.Add(new TextBlock{Text="Layers",FontSize=13,Margin=new(0,0,0,6)});
            foreach(var layer in _document.Layers)
            {
                var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                var visible=new CheckBox{Content=layer.Name,IsChecked=layer.Visible,FontSize=11,MinWidth=90};visible.Click+=(_,_)=>Change("Layer visibility",d=>d.UpdateLayer(d.GetLayer(layer.Id) with{Visible=visible.IsChecked==true}));row.Children.Add(visible);
                var locked=new CheckBox{Content="Lock",IsChecked=layer.Locked,FontSize=10};locked.Click+=(_,_)=>Change("Layer lock",d=>d.UpdateLayer(d.GetLayer(layer.Id) with{Locked=locked.IsChecked==true}));SetColumn(locked,1);row.Children.Add(locked);_layers.Children.Add(row);
            }
            var add=new Button{Content="New layer",FontSize=11,MinHeight=24,Padding=new(6,2,6,2)};add.Click+=(_,_)=>Change("New layer",d=>d.AddLayer($"Layer {d.Layers.Count}",ColorRgba.Default));_layers.Children.Add(add);
            _objects.ItemsSource=_document.Objects;_objects.SelectedItems.Clear();foreach(var o in _document.Objects.Where(o=>_document.Selection.Contains(o.Id)))_objects.SelectedItems.Add(o);
            var selection=_document.Objects.Where(o=>_document.Selection.Contains(o.Id)).ToArray();
            void Label(string text)=>_properties.Children.Add(new TextBlock{Text=text,FontSize=11,TextWrapping=TextWrapping.Wrap});
            if(selection.Length!=1){Label(selection.Length==0?"Document properties":$"{selection.Length} objects selected");Label($"Units: {_document.Units}");Label($"Absolute tolerance: {_document.AbsoluteTolerance:G}");Label($"Objects: {_document.Objects.Count}");return;}
            var value=selection[0];var name=new TextBox{Header="Object name",Text=value.Name,FontSize=12};name.LostFocus+=(_,_)=>Change("Rename object",d=>d.Update(d.GetObject(value.Id) with{Name=name.Text}));_properties.Children.Add(name);
            var layerChoice=new ComboBox{Header="Layer",ItemsSource=_document.Layers,DisplayMemberPath="Name",SelectedItem=_document.GetLayer(value.LayerId),HorizontalAlignment=HorizontalAlignment.Stretch,FontSize=12};layerChoice.SelectionChanged+=(_,_)=>{if(!_updating&&layerChoice.SelectedItem is Layer layer)Change("Object layer",d=>d.Update(d.GetObject(value.Id) with{LayerId=layer.Id}));};_properties.Children.Add(layerChoice);
            Label(value.Geometry switch{NurbsCurve c=>$"NURBS curve · degree {c.Degree}\n{c.Count} weighted controls\nDomain [{c.Domain.Min:G}, {c.Domain.Max:G}]",NurbsSurface s=>$"NURBS surface · degree {s.DegreeU} × {s.DegreeV}\n{s.CountU} × {s.CountV} weighted controls",_=>value.Geometry.GetType().Name});
            var size=value.Geometry.Bounds.Size;Label($"Control-hull bounds: {size.X:G5} × {size.Y:G5} × {size.Z:G5}");
        }
        finally{_updating=false;}
    }
    public void Detach(){if(_document is not null)_document.Changed-=OnChanged;_document=null;}
}
