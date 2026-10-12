using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
using SkiaSharp;

namespace NurbsScene.Controls;

public sealed partial class GraphEditorControl
{
    private NumberSliderDialog? _numberSliderDialog;
    public FrameworkElement? NumberSliderEditor => _numberSliderDialog;
    public bool NumberSliderEditorIsOpen => _numberSliderDialog?.IsLoaded==true;
    public string NumberSliderValidation => _numberSliderDialog?.ValidationMessage??"";

    private async Task EditNumberSliderAsync(string id)
    {
        if(Session is null||XamlRoot is null||_numberSliderDialog is not null||Canvas.IsInteracting)return;
        var session=Session;var graph=session.Graph;var node=graph.GetNode(id);
        try
        {
            CloseActionPopups();
            var dialog=new NumberSliderDialog(node){XamlRoot=XamlRoot};_numberSliderDialog=dialog;
            if(await dialog.ShowAsync()==ContentDialogResult.Primary&&dialog.Edit is{} edit)
            {
                // Preserve the exact target captured before opening; other hosts
                // can update or replace the graph while a modal editor is open.
                if(!ReferenceEquals(Session,session))throw new InvalidOperationException("The definition session changed. Reopen the slider editor.");
                NumberSliderEditing.Apply(session,graph,node,edit.Settings,edit.Value,edit.Name);
                RefreshInspector();UpdateStatus();
            }
        }
        catch(Exception error){Report(error.Message);}
        finally{_numberSliderDialog=null;Canvas.Focus(FocusState.Programmatic);}
    }
}

public sealed partial class GraphCanvas
{
    public event Action<string>? SliderEditRequested;
    private void DrawNumberSlider(SKCanvas canvas,GraphNode node,SKRect rect)
    {
        if(!NumberSliderSettings.TryRead(node,out var settings,out var value,out _))
        {
            Text(canvas,node.Name??node.Component,rect.Left+9,rect.Top+14,new(50,50,54),10);
            Text(canvas,"Invalid slider settings",rect.Left+9,rect.Bottom-7,new(174,57,31),10);return;
        }
        var fraction=settings.Fraction(value);
        Text(canvas,node.Name??node.Component,rect.Left+9,rect.Top+14,new(50,50,54),10);
        _font.Size=10;var label=value.ToString("G7",System.Globalization.CultureInfo.InvariantCulture);
        Text(canvas,label,rect.Right-10-_font.MeasureText(label),rect.Top+14,new(40,40,43),10);
        Fill(canvas,new(rect.Left+13,rect.Bottom-12,rect.Right-13,rect.Bottom-8),new(157,157,161),2);
        var x=rect.Left+13+(float)fraction*(rect.Width-26);
        Fill(canvas,new(x-3,rect.Bottom-16,x+3,rect.Bottom-4),new(230,231,223),2);
        Stroke(canvas,new(x-3,rect.Bottom-16,x+3,rect.Bottom-4),new(78,78,79),1,2);
    }
}
