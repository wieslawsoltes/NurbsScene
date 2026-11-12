using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;

namespace NurbsScene.Controls;

/// <summary>Shared compact desktop chrome for independently reusable controls.</summary>
public static class ClassicChrome
{
    public static SolidColorBrush Brush(byte r,byte g,byte b)=>new(ColorHelper.FromArgb(255,r,g,b));
    public static SolidColorBrush Background {get;}=Brush(232,233,236);
    public static SolidColorBrush PanelBackground {get;}=Brush(242,242,243);
    public static SolidColorBrush Surface {get;}=Brush(255,255,255);
    public static SolidColorBrush Border {get;}=Brush(180,184,190);
    public static SolidColorBrush Ink {get;}=Brush(38,41,46);
    public static SolidColorBrush MutedInk {get;}=Brush(100,105,113);
    public static SolidColorBrush Selected {get;}=Brush(196,216,240);
    public static SolidColorBrush Accent {get;}=Brush(47,101,164);
    public static SolidColorBrush Hover {get;}=Brush(219,228,239);

    /// <summary>Keep native control templates and their accessibility/input behavior,
    /// with the same compact dimensions and state palette in every workspace panel.</summary>
    public static void Install(ResourceDictionary resources)
    {
        resources["ControlContentThemeFontSize"]=12d;
        resources["ControlCornerRadius"]=new CornerRadius(0);
        resources["OverlayCornerRadius"]=new CornerRadius(2);
        resources["ButtonPadding"]=new Thickness(7,2,7,2);
        resources["TextControlThemePadding"]=new Thickness(5,2,5,2);
        resources["ComboBoxThemePadding"]=new Thickness(5,2,24,2);
        resources["CheckBoxMinHeight"]=24d;
        resources["CheckBoxMinWidth"]=0d;
        resources["ListViewItemMinHeight"]=24d;
        resources["ButtonBackgroundPointerOver"]=Hover;
        resources["ButtonBackgroundPressed"]=Selected;
        resources["ButtonBorderBrushPointerOver"]=Accent;
        resources["ButtonBorderBrushPressed"]=Accent;
        resources["TextControlBackground"]=Surface;
        resources["TextControlBackgroundPointerOver"]=Surface;
        resources["TextControlBackgroundFocused"]=Surface;
        resources["TextControlBorderBrush"]=Border;
        resources["ComboBoxBackground"]=Surface;
        resources["ComboBoxBorderBrush"]=Border;
    }
    public static Button Button(string text,Action action,string? id=null)
    {
        var button=new Button{Content=text,FontSize=11,MinWidth=0,MinHeight=0,Height=25,Padding=new(7,2,7,2),CornerRadius=new(0),BorderThickness=new(1),BorderBrush=Border,Background=PanelBackground,Foreground=Ink};
        button.Click+=(_,_)=>action();
        if(id is not null)AutomationProperties.SetAutomationId(button,id);
        AutomationProperties.SetName(button,text);return button;
    }
    public static Button IconButton(string label,string icon,Action action,string? id=null)
    {
        var button=Button(label,action,id);button.Content=new WorkbenchIcon(icon);button.Width=27;button.Height=27;button.Padding=new(2);button.BorderThickness=new(0);
        ToolTipService.SetToolTip(button,label);return button;
    }
    public static Button TabButton(string label,Action action,string? id=null)
    {
        var button=Button(label,action,id);button.Height=24;button.Padding=new(9,1,9,2);
        button.BorderThickness=new(0,0,0,1);button.Background=Background;
        return button;
    }
    public static void SelectTab(Button button,bool selected)
    {
        button.Background=selected?Surface:Background;
        button.BorderBrush=selected?Accent:Border;
        button.BorderThickness=new(0,0,0,selected?2:1);
        button.Foreground=selected?Ink:MutedInk;
    }
    public static Border Separator(bool vertical=true)=>new(){
        Background=Border,Width=vertical?1:double.NaN,Height=vertical?22:1,
        Margin=vertical?new Thickness(5,2,5,2):new Thickness(0,3,0,3)};
    public static Border SectionHeader(string text)
    {
        var label=Label(text);label.FontWeight=FontWeights.SemiBold;label.Margin=new(7,3,5,3);
        return new Border{Child=label,Background=Background,BorderBrush=Border,BorderThickness=new(0,0,0,1)};
    }
    public static TextBlock Label(string text,int size=11)=>new(){Text=text,FontSize=size,Foreground=Ink,VerticalAlignment=VerticalAlignment.Center};
    public static ScrollViewer Horizontal(UIElement content)=>new(){Content=content,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollMode=ScrollMode.Enabled,VerticalScrollMode=ScrollMode.Disabled};
}
