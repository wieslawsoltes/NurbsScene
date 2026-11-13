using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Presentation;

namespace NurbsScene.Controls;

public sealed partial class WorkspaceView
{
    private const double SidebarWidth=116;
    private readonly ContentControl _sidebarTools=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch};
    private readonly TextBlock _sidebarHeading=ClassicChrome.Label("Standard",10);
    private readonly Dictionary<string,Grid> _sidebarPages=[];

    private ScrollViewer CreateModelSidebar(Button endSnap)
    {
        var content=new StackPanel{Spacing=0};
        var heading=new Grid{Height=21,Background=ClassicChrome.Background,BorderBrush=ClassicChrome.Border,BorderThickness=new(0,0,0,1)};
        heading.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        heading.ColumnDefinitions.Add(new(){Width=new GridLength(19)});
        _sidebarHeading.FontWeight=FontWeights.Normal;_sidebarHeading.Foreground=ClassicChrome.MutedInk;_sidebarHeading.Margin=new(5,0,0,0);
        _sidebarHeading.TextTrimming=TextTrimming.CharacterEllipsis;heading.Children.Add(_sidebarHeading);
        var hide=ClassicChrome.IconButton("Hide modeling sidebar","Close",()=>Run("ToggleLeftSidebar"),"HideModelSidebar");
        hide.Width=18;hide.Height=18;hide.Padding=new(4);hide.BorderThickness=new(0);hide.Background=ClassicChrome.Transparent;
        hide.Content=new WorkbenchIcon("Close"){Width=9,Height=9};
        heading.Children.Add(hide);Grid.SetColumn(hide,1);content.Children.Add(heading);content.Children.Add(_sidebarTools);
        content.Children.Add(ClassicChrome.Separator(false));content.Children.Add(ClassicChrome.SectionHeader("Osnap"));
        endSnap.HorizontalAlignment=HorizontalAlignment.Stretch;endSnap.HorizontalContentAlignment=HorizontalAlignment.Left;
        endSnap.BorderThickness=new(0);endSnap.Margin=new(3,1,3,0);endSnap.Height=22;endSnap.Padding=new(5,0,5,0);
        ToolTipService.SetToolTip(endSnap,"Snap to curve endpoints");content.Children.Add(endSnap);
        var options=ClassicChrome.Button("Grid settings…",()=>_ =SafeAsync(GridOptionsAsync),"GridOptions");
        options.Margin=new(3,2,3,3);options.Height=22;options.HorizontalAlignment=HorizontalAlignment.Stretch;
        content.Children.Add(options);
        var frame=new Border{Child=content,Background=ClassicChrome.PanelBackground,BorderBrush=ClassicChrome.Border,BorderThickness=new(0,0,1,0)};
        var scroll=new ScrollViewer{Content=frame,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
            HorizontalScrollMode=ScrollMode.Disabled,VerticalScrollMode=ScrollMode.Enabled};
        AutomationProperties.SetAutomationId(scroll,"ModelSidebar");
        Toolbar.CategoryChanged+=SelectSidebarCategory;SelectSidebarCategory(Toolbar.Category);
        return scroll;
    }
    private void SelectSidebarCategory(string category)
    {
        if(!_sidebarPages.TryGetValue(category,out var page))
        {
            page=new Grid{Margin=new(2,2,2,2)};
            for(var i=0;i<4;i++)page.ColumnDefinitions.Add(new(){Width=new GridLength(27)});
            var common=new[]{"SelectAll","SelectNone"}.Select(id=>WorkspaceCommands.Tools.Single(tool=>tool.Id==id));
            var tools=category=="Standard"?WorkspaceCommands.Tools.Where(tool=>tool.Category is "Curve" or "Surface" or "Transform" or "Analyze")
                :ModelToolbar.CategoryTools(category);
            var all=common.Concat(tools).DistinctBy(tool=>tool.Id).ToArray();
            for(var i=0;i<(all.Length+3)/4;i++)page.RowDefinitions.Add(new(){Height=new GridLength(26)});
            for(var i=0;i<all.Length;i++)
            {
                var tool=all[i];var button=ClassicChrome.IconButton(tool.Label,tool.Icon,()=>InvokeTool(tool),"Side_"+tool.Id);
                button.Width=26;button.Height=26;button.HorizontalAlignment=HorizontalAlignment.Center;
                page.Children.Add(button);Grid.SetColumn(button,i%4);Grid.SetRow(button,i/4);
            }
            _sidebarPages.Add(category,page);
        }
        _sidebarHeading.Text=ModelToolbar.CategoryLabel(category);
        if(!ReferenceEquals(_sidebarTools.Content,page))_sidebarTools.Content=page;
    }
    private static void StyleStatusButton(Button button)
    {
        button.Height=22;button.BorderThickness=new(0,0,1,0);button.Padding=new(6,0,6,0);
        button.Background=ClassicChrome.PanelBackground;button.BorderBrush=ClassicChrome.Border;
    }
    private void UpdateViewportTabs()
    {
        var active=Viewport.Canvas.ActiveViewport;
        if(_activeViewportTab==active)return;
        _activeViewportTab=active;
        foreach(var (name,button) in _viewportTabs)ClassicChrome.SelectTab(button,name==active);
    }
}
