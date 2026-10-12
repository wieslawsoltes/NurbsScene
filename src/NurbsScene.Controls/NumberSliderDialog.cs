using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Parametric;
using NurbsScene.Presentation;

namespace NurbsScene.Controls;

public sealed record NumberSliderEdit(NumberSliderSettings Settings, double Value, string? Name);

/// <summary>Draft-only number-slider editor. Range, precision, value and nickname
/// are validated together; invalid Apply keeps the dialog open. Showing, typing,
/// cancelling and closing this control never changes an application graph.</summary>
public sealed class NumberSliderDialog : ContentDialog
{
    private readonly TextBox _name, _minimum, _maximum, _value;
    private readonly ComboBox _precision;
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = ClassicChrome.Brush(170,45,30) };
    public NumberSliderEdit? Edit { get; private set; }
    public string ValidationMessage => _error.Text;

    public NumberSliderDialog(GraphNode source)
    {
        if (source.Component is not ("Number" or "Integer")) throw new ArgumentException("Select a number slider.",nameof(source));
        Title = "Number Slider"; PrimaryButtonText = "Apply"; CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary; FontSize = 12;
        AutomationProperties.SetAutomationId(this,"NumberSliderDialog");
        string Parameter(string key,string fallback) => source.Parameters.TryGetValue(key,out var value)
            ? value switch { double d => d.ToString("R",CultureInfo.InvariantCulture), int i => i.ToString(CultureInfo.InvariantCulture), _ => "Invalid" } : fallback;
        TextBox Field(string label,string text,string id)
        {
            var field = new TextBox { Header=label,Text=text,FontSize=12,MinHeight=28,Padding=new(6,3,6,3) };
            AutomationProperties.SetAutomationId(field,id);AutomationProperties.SetName(field,label);return field;
        }
        _name=Field("Name",source.Name??source.Component,"SliderName");_name.MaxLength=256;
        _minimum=Field("Minimum",Parameter("min","0"),"SliderMinimum");
        _maximum=Field("Maximum",Parameter("max","100"),"SliderMaximum");
        _value=Field("Value",Parameter("value","1"),"SliderValue");
        var integer=source.Component=="Integer";
        _precision=new ComboBox { Header="Decimal places",FontSize=12,HorizontalAlignment=HorizontalAlignment.Stretch,
            ItemsSource=new[]{"Automatic (continuous)"}.Concat(Enumerable.Range(0,13).Select(i=>i.ToString(CultureInfo.InvariantCulture))).ToArray(),
            SelectedIndex=0,IsEnabled=!integer };
        if(NumberSliderSettings.TryRead(source,out var settings,out _,out _)&&settings.DecimalPlaces is{} places)_precision.SelectedIndex=places+1;
        if(integer)_precision.SelectedIndex=1;
        AutomationProperties.SetAutomationId(_precision,"SliderPrecision");
        AutomationProperties.SetAutomationId(_error,"SliderValidation");
        var content=new StackPanel { Spacing=9,Width=350 };
        content.Children.Add(ClassicChrome.Label(integer?"Integer · signed 32-bit":"Real · binary64",11));content.Children.Add(_name);
        var range=new Grid { ColumnSpacing=10 };range.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});range.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        range.Children.Add(_minimum);range.Children.Add(_maximum);Grid.SetColumn(_maximum,1);content.Children.Add(range);
        content.Children.Add(_value);content.Children.Add(_precision);
        content.Children.Add(new TextBlock { Text="Apply updates all settings in one undoable edit. Cancel preserves the current definition.",TextWrapping=TextWrapping.Wrap,FontSize=10 });
        content.Children.Add(_error);Content=content;
        PrimaryButtonClick+=(_,args)=>
        {
            try
            {
                static double Parse(TextBox field)
                {
                    if(!double.TryParse(field.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)||!double.IsFinite(number))
                        throw new ArgumentException($"{field.Header} must be a finite number (use a dot as the decimal separator).");
                    return number;
                }
                if(_precision.SelectedIndex<0)throw new ArgumentException("Select a decimal precision.");
                var options=new NumberSliderSettings(Parse(_minimum),Parse(_maximum),_precision.SelectedIndex==0?null:_precision.SelectedIndex-1,integer);
                var value=options.Normalize(Parse(_value));var name=_name.Text.Trim();
                Edit=new(options,value,name.Length==0||source.Name is null&&name==source.Component?null:name);_error.Text="";
            }
            catch(ArgumentException error){args.Cancel=true;Edit=null;_error.Text=error.Message;}
        };
    }
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if(GetTemplateChild("PrimaryButton") is FrameworkElement apply)AutomationProperties.SetAutomationId(apply,"SliderApply");
        if(GetTemplateChild("CloseButton") is FrameworkElement cancel)AutomationProperties.SetAutomationId(cancel,"SliderCancel");
    }
}
