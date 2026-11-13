using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.System;

namespace NurbsScene.Controls;

/// <summary>Shared compact desktop chrome for independently reusable controls.</summary>
public static class ClassicChrome
{
    public static SolidColorBrush Brush(byte r,byte g,byte b)=>new(ColorHelper.FromArgb(255,r,g,b));
    public static SolidColorBrush Background {get;}=Brush(236,236,236);
    public static SolidColorBrush PanelBackground {get;}=Brush(244,244,244);
    public static SolidColorBrush Surface {get;}=Brush(255,255,255);
    public static SolidColorBrush Transparent {get;}=new(Colors.Transparent);
    public static SolidColorBrush Border {get;}=Brush(181,184,188);
    public static SolidColorBrush Ink {get;}=Brush(42,42,42);
    public static SolidColorBrush MutedInk {get;}=Brush(104,104,104);
    public static SolidColorBrush Selected {get;}=Brush(196,216,240);
    public static SolidColorBrush Accent {get;}=Brush(47,101,164);
    public static SolidColorBrush ActiveTitle {get;}=Brush(145,183,235);
    public static SolidColorBrush Hover {get;}=Brush(225,229,233);

    /// <summary>Keep native control templates and their accessibility/input behavior,
    /// with the same compact dimensions and state palette in every workspace panel.</summary>
    public static void Install(ResourceDictionary resources)
    {
        resources["ControlContentThemeFontSize"]=12d;
        resources["ControlCornerRadius"]=new CornerRadius(0);
        resources["OverlayCornerRadius"]=new CornerRadius(2);
        resources["ButtonPadding"]=new Thickness(6,1,6,1);
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
        var button=new Button{Content=text,FontSize=11,MinWidth=0,MinHeight=0,Height=24,Padding=new(6,1,6,1),CornerRadius=new(0),BorderThickness=new(1),BorderBrush=Border,Background=PanelBackground,Foreground=Ink};
        button.Click+=(_,_)=>action();
        if(id is not null)AutomationProperties.SetAutomationId(button,id);
        AutomationProperties.SetName(button,text);return button;
    }
    public static Button IconButton(string label,string icon,Action action,string? id=null)
    {
        var button=Button(label,action,id);button.Content=new WorkbenchIcon(icon);button.Width=26;button.Height=26;button.Padding=new(1);
        // A native toolbar has one continuous face; individual cells appear only
        // while interacting. Keep the normal and hover border widths identical.
        button.Background=Transparent;button.BorderBrush=Transparent;
        button.Resources["ButtonBorderBrushPointerOver"]=Border;
        button.Resources["ButtonBorderBrushPressed"]=Accent;
        ToolTipService.SetToolTip(button,label);return button;
    }
    public static Button TabButton(string label,Action action,string? id=null)
    {
        var button=Button(label,action,id);button.Height=22;button.Padding=new(7,0,7,1);
        button.BorderThickness=new(1,1,1,0);button.BorderBrush=Transparent;button.Background=Background;
        button.Resources["ButtonBackgroundPointerOver"]=PanelBackground;
        button.Resources["ButtonBorderBrushPointerOver"]=Border;
        button.Resources["ButtonBorderBrushPressed"]=Border;
        return button;
    }
    public static void SelectTab(Button button,bool selected)
    {
        button.Background=selected?PanelBackground:Background;
        button.BorderBrush=selected?Border:Transparent;
        // The border occupies the same space in both states, so tab selection
        // never shifts neighboring hit targets or adds a web-style underline.
        button.BorderThickness=new(1,1,1,0);
        button.Foreground=selected?Ink:MutedInk;
    }
    public static Border Separator(bool vertical=true)=>new(){
        Background=Border,Width=vertical?1:double.NaN,Height=vertical?18:1,
        Margin=vertical?new Thickness(3,4,3,4):new Thickness(0,1,0,1)};
    public static Border SectionHeader(string text)
    {
        var label=Label(text);label.FontWeight=FontWeights.Normal;label.Margin=new(5,2,5,2);
        return new Border{Child=label,Background=Background,BorderBrush=Border,BorderThickness=new(0,0,0,1)};
    }
    public static TextBlock Label(string text,int size=11)=>new(){Text=text,FontSize=size,Foreground=Ink,VerticalAlignment=VerticalAlignment.Center};
    public static ScrollViewer Horizontal(UIElement content)=>new(){Content=content,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollMode=ScrollMode.Enabled,VerticalScrollMode=ScrollMode.Disabled};

    /// <summary>A native horizontal viewport for a fixed-height chrome row.
    /// Hiding the bar avoids reserving part of the row for scrollbar layout;
    /// keyboard focus, touch, horizontal wheels and trackpads remain native.
    /// Null content supports a retained host whose cached page is assigned later.</summary>
    public static ScrollViewer CompactHorizontal(UIElement? content)
    {
        var viewer=new ScrollViewer{
            Content=content,Padding=new(0),BorderThickness=new(0),
            HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,
            HorizontalScrollMode=ScrollMode.Enabled,VerticalScrollMode=ScrollMode.Disabled,
            BringIntoViewOnFocusChange=true};
        viewer.PointerWheelChanged+=(_,args)=>{
            // Native ScrollViewer handles horizontal input and Shift+wheel.
            // Redirect only an otherwise unhandled ordinary wheel; do not take
            // input from a nested editor, modified gesture, or an ancestor at an edge.
            if(args.Handled||args.KeyModifiers!=VirtualKeyModifiers.None||viewer.ScrollableWidth<=0)return;
            var properties=args.GetCurrentPoint(viewer).Properties;
            if(properties.IsHorizontalMouseWheel||properties.MouseWheelDelta==0)return;
            var target=Math.Clamp(viewer.HorizontalOffset-properties.MouseWheelDelta*.4,0,viewer.ScrollableWidth);
            if(target!=viewer.HorizontalOffset&&viewer.ChangeView(target,null,null,disableAnimation:true))args.Handled=true;
        };
        return viewer;
    }
}
