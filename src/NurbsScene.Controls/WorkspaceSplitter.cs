using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace NurbsScene.Controls;

/// <summary>Focusable single-owner splitter. Cancel/capture loss restores the
/// draft and never writes preferences; only an initiating-button release commits.</summary>
public sealed class WorkspaceSplitter : ContentControl
{
    public UIElement? RelativeTo {get;set;}
    public Func<double>? ReadValue {get;set;}
    public Func<Point,double>? ValueAt {get;set;}
    public Action<double,bool>? ApplyValue {get;set;}
    public double Minimum {get;set;}=.2;
    public double Maximum {get;set;}=.8;
    public double Step {get;set;}=.02;
    public double Direction {get;set;}=1;
    private uint? _pointer;
    private double _before;
    public WorkspaceSplitter()
    {
        MinWidth=0;MinHeight=0;Padding=new(0);IsTabStop=true;
        HorizontalContentAlignment=HorizontalAlignment.Stretch;VerticalContentAlignment=VerticalAlignment.Stretch;
        Content=new Border{Background=ClassicChrome.Border};
        AutomationProperties.SetHelpText(this,"Drag or use arrow keys to resize. Escape restores the size before dragging.");
        PointerPressed+=(_,e)=>{
            if(_pointer is not null){e.Handled=true;return;}
            var p=e.GetCurrentPoint(this);
            if(!p.Properties.IsLeftButtonPressed&&e.Pointer.PointerDeviceType.ToString()!="Touch")return;
            if(ReadValue is null||!CapturePointer(e.Pointer))return;
            _before=ReadValue();_pointer=e.Pointer.PointerId;Focus(FocusState.Pointer);e.Handled=true;
        };
        PointerMoved+=(_,e)=>{
            if(_pointer!=e.Pointer.PointerId||RelativeTo is null||ValueAt is null)return;
            var p=e.GetCurrentPoint(RelativeTo);
            if(e.Pointer.PointerDeviceType.ToString()=="Mouse"&&!p.Properties.IsLeftButtonPressed){Cancel();return;}
            Set(ValueAt(p.Position),false);e.Handled=true;
        };
        PointerReleased+=(_,e)=>{
            if(_pointer!=e.Pointer.PointerId||e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
            var value=ReadValue?.Invoke()??_before;_pointer=null;ReleasePointerCapture(e.Pointer);Set(value,true);e.Handled=true;
        };
        PointerCanceled+=(_,e)=>{if(_pointer==e.Pointer.PointerId)Cancel();};
        PointerCaptureLost+=(_,e)=>{if(_pointer==e.Pointer.PointerId)Cancel();};
        KeyDown+=(_,e)=>{
            if(e.Key==VirtualKey.Escape&&_pointer is not null){Cancel();e.Handled=true;return;}
            if(_pointer is not null||ReadValue is null)return;
            var delta=e.Key is VirtualKey.Left or VirtualKey.Up?-Step:e.Key is VirtualKey.Right or VirtualKey.Down?Step:0;
            if(delta==0)return;Set(ReadValue()+delta*Direction,true);e.Handled=true;
        };
        Unloaded+=(_,_)=>Cancel();
    }
    private void Set(double value,bool persist){if(double.IsFinite(value))ApplyValue?.Invoke(Math.Clamp(value,Minimum,Maximum),persist);}
    public void Cancel(){if(_pointer is null)return;_pointer=null;Set(_before,false);ReleasePointerCaptures();}
}
