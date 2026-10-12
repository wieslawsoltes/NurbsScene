using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using SkiaSharp;
using Windows.System;

namespace NurbsScene.Controls;

public sealed partial class GraphEditorControl
{
    private NumberSliderDialog? _numberSliderDialog;
    private bool _sliderEditorDisposed;
    public FrameworkElement? NumberSliderEditor => _numberSliderDialog;
    public bool NumberSliderEditorIsOpen => _numberSliderDialog?.IsLoaded==true;
    public string NumberSliderValidation => _numberSliderDialog?.ValidationMessage??"";

    private void QueueNumberSliderEdit(Graph graph,string id)
    {
        // A double-tap can arrive before the second pointer release. Dispatch
        // after input completion, but never retarget an undo/load replacement.
        DispatcherQueue.TryEnqueue(()=>{
            if(!_sliderEditorDisposed&&ReferenceEquals(Session?.Graph,graph))_ =EditNumberSliderAsync(id);
        });
    }
    private async Task EditNumberSliderAsync(string id)
    {
        if(_sliderEditorDisposed||Session is null||XamlRoot is null||_numberSliderDialog is not null||Canvas.IsInteracting)return;
        var session=Session;var graph=session.Graph;
        try
        {
            var node=graph.GetNode(id);
            CloseActionPopups();
            var dialog=new NumberSliderDialog(node){XamlRoot=XamlRoot};_numberSliderDialog=dialog;
            if(await dialog.ShowAsync()==ContentDialogResult.Primary&&dialog.Edit is{} edit)
            {
                if(_sliderEditorDisposed||!ReferenceEquals(Session,session))throw new InvalidOperationException("The definition session changed. Reopen the slider editor.");
                NumberSliderEditing.Apply(session,graph,node,edit.Settings,edit.Value,edit.Name);
                RefreshInspector();UpdateStatus();
            }
        }
        catch(Exception error){if(!_sliderEditorDisposed)Report(error.Message);}
        finally{_numberSliderDialog=null;if(!_sliderEditorDisposed)Canvas.Focus(FocusState.Programmatic);}
    }
    private void DisposeSliderEditor(){_sliderEditorDisposed=true;_numberSliderDialog?.Hide();}
}

public sealed partial class GraphCanvas
{
    public event Action<string>? SliderEditRequested;
    private void OnCanvasDoubleTapped(object sender,DoubleTappedRoutedEventArgs e)
    {
        _worldLast=World(e.GetPosition(this));var node=NodeAt(_worldLast);
        if(node is null)SearchRequested?.Invoke();
        else if(node.Component is "Number" or "Integer")
        {
            // The label opens settings. The rail remains a direct scrub target,
            // including repeated clicks; opening settings must not change values.
            if(_worldLast.Y<node.Y+16){CancelGesture();ReleasePointerCaptures();SelectNode(node.Id);SliderEditRequested?.Invoke(node.Id);}
        }
        else if(ResizeHit(node,_worldLast)==PanelResizeEdges.None)HandleValueDoubleTap(node);
        e.Handled=true;
    }
    private void DrawNumberSlider(SKCanvas canvas,GraphNode node,SKRect rect)
    {
        if(!NumberSliderSettings.TryRead(node,out var settings,out var value,out _))
        {
            Text(canvas,node.Name??node.Component,rect.Left+9,rect.Top+14,new(50,50,54),10);
            Text(canvas,"Invalid slider settings",rect.Left+9,rect.Bottom-7,new(174,57,31),10);return;
        }
        var fraction=settings.Fraction(value);
        Text(canvas,node.Name??node.Component,rect.Left+9,rect.Top+14,new(50,50,54),10);
        _font.Size=10;var label=value.ToString(settings.DecimalPlaces is{} digits?"F"+digits:"G7",System.Globalization.CultureInfo.InvariantCulture);
        Text(canvas,label,rect.Right-10-_font.MeasureText(label),rect.Top+14,new(40,40,43),10);
        Fill(canvas,new(rect.Left+13,rect.Bottom-12,rect.Right-13,rect.Bottom-8),new(157,157,161),2);
        var x=rect.Left+13+(float)fraction*(rect.Width-26);
        Fill(canvas,new(x-3,rect.Bottom-16,x+3,rect.Bottom-4),new(230,231,223),2);
        Stroke(canvas,new(x-3,rect.Bottom-16,x+3,rect.Bottom-4),new(78,78,79),1,2);
    }
}
