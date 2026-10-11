using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace NurbsScene.Controls;

/// <summary>Shared compact desktop chrome for independently reusable controls.</summary>
public static class ClassicChrome
{
    public static SolidColorBrush Brush(byte r,byte g,byte b)=>new(ColorHelper.FromArgb(255,r,g,b));
    public static SolidColorBrush Background=>Brush(237,237,237);
    public static SolidColorBrush Border=>Brush(177,177,177);
    public static SolidColorBrush Ink=>Brush(40,40,40);
    public static SolidColorBrush Selected=>Brush(210,226,239);
    public static Button Button(string text,Action action,string? id=null)
    {
        var button=new Button{Content=text,FontSize=11,MinWidth=0,MinHeight=0,Height=25,Padding=new(7,2,7,2),CornerRadius=new(0),BorderThickness=new(1),BorderBrush=Border,Background=Background,Foreground=Ink};
        button.Click+=(_,_)=>action();
        if(id is not null)AutomationProperties.SetAutomationId(button,id);
        AutomationProperties.SetName(button,text);return button;
    }
    public static Button IconButton(string label,string icon,Action action,string? id=null)
    {
        var button=Button(label,action,id);button.Content=new WorkbenchIcon(icon);button.Width=27;button.Height=27;button.Padding=new(2);button.BorderThickness=new(0);
        ToolTipService.SetToolTip(button,label);return button;
    }
    public static TextBlock Label(string text,int size=11)=>new(){Text=text,FontSize=size,Foreground=Ink,VerticalAlignment=VerticalAlignment.Center};
    public static ScrollViewer Horizontal(UIElement content)=>new(){Content=content,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollMode=ScrollMode.Enabled,VerticalScrollMode=ScrollMode.Disabled};
}
