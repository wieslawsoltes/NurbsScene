using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using NurbsScene.Controls.Drawing;
using Windows.Foundation;
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
        // The canvas requests editing after its gesture completes. Keep modal
        // work out of routed-event delivery and never retarget a replaced graph.
        DispatcherQueue.TryEnqueue(()=>{
            if(!_sliderEditorDisposed&&ReferenceEquals(Session?.Graph,graph))_ =EditNumberSliderAsync(id);
        });
    }
    private async Task EditNumberSliderAsync(string id)
    {
        if(_sliderEditorDisposed||Session is null||XamlRoot is null||_numberSliderDialog is not null||_panelTypographyDialog is not null||_textPanelDialog is not null||Canvas.IsInteracting)return;
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
    private sealed record DoubleTapActivation(Graph Graph,string? NodeId,Point World,VirtualKeyModifiers Modifiers);
    private DoubleTapActivation? _pendingDoubleTap;
    private void OnCanvasDoubleTapped(object sender,DoubleTappedRoutedEventArgs e)
    {
        if(_session is null||_canvasDisposed)return;
        var point=e.GetPosition(this);
        _worldLast=World(point);
        var activation=new DoubleTapActivation(_session.Graph,NodeAt(_worldLast)?.Id,_worldLast,_lastPointerModifiers);
        // Uno can recognize a double tap on the second press. Opening a dialog
        // then would steal capture from a rapid select-and-drag. Retain the
        // activation until the owning pointer releases without becoming a drag.
        if(_activePointerId is not null)
        {
            if(_pointerButton==1&&!_moved)
            {_pendingDoubleTap=activation;Observe("DoubleTapDeferred",point);}
        }
        else if(!_moved)ActivateDoubleTap(activation); // Hosts that recognize the completed tap on release.
        e.Handled=true;
    }
    private void ActivateDoubleTap(DoubleTapActivation activation)
    {
        if(_canvasDisposed||_session is null||!ReferenceEquals(_session.Graph,activation.Graph))return;
        if(activation.NodeId is null){SearchRequested?.Invoke();return;}
        if(!_session.Graph.TryGetNode(activation.NodeId,out var node))return;
        _worldLast=activation.World;
        if(node.Component is "Number" or "Integer")
        {
            // The label opens settings. The rail remains a direct scrub target,
            // including repeated clicks; opening settings must not change values.
            if(_worldLast.Y<node.Y+16){CancelGesture();ReleasePointerCaptures();SelectNode(node.Id);SliderEditRequested?.Invoke(node.Id);}
        }
        else if(ResizeHit(node,_worldLast)==PanelResizeEdges.None)
        {
            if((node.Component is "Panel" or "Text") && (activation.Modifiers & VirtualKeyModifiers.Control)!=0)
            {CancelGesture();ReleasePointerCaptures();SelectNode(node.Id);PanelTypographyRequested?.Invoke(node.Id);}
            else HandleValueDoubleTap(node);
        }
    }
    private void DrawNumberSlider(VectorCanvas canvas,GraphNode node,VectorRect rect)
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
