using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;
using NurbsScene.Presentation;

namespace NurbsScene.Controls;

public sealed partial class GraphEditorControl
{
    private StackPanel? _searchPanel;
    private readonly CanvasSearchIndex _canvasSearchIndex=new();
    private void ShowCanvasSearch()
    {
        if(Session is null||XamlRoot is null||_valueEditorDisposed)return;
        if(_canvasSearch?.IsOpen==true){_popupSearch?.Focus(FocusState.Programmatic);return;}
        var rootWidth=XamlRoot.Size.Width;var rootHeight=XamlRoot.Size.Height;
        if(rootWidth<=0||rootHeight<=0)return;
        var session=Session;var graph=session.Graph;var insertion=Canvas.InsertionPoint;
        // A closed AutoSuggestBox can retain its native editor text/undo state
        // after a deferred TextChanged. Resetting only its outer Text property
        // is not a reliable new editing session. Replace the editor per opening;
        // the immutable registry/search-result index remains reusable.
        _canvasSearch?.Hide();
        _searchPanel?.Children.Remove(_creationInfo);
        var search=new AutoSuggestBox{PlaceholderText="Search components…",Width=Math.Max(1,Math.Min(290,rootWidth-64)),
            FontSize=12,MaxSuggestionListHeight=180,ItemsSource=Array.Empty<string>()};
        AutomationProperties.SetAutomationId(search,"CanvasComponentSearch");
        var panel=new StackPanel{Spacing=5};AutomationProperties.SetAutomationId(panel,"CanvasSearchPanel");
        panel.Children.Add(ClassicChrome.Label("Search",11));panel.Children.Add(search);
        _creationInfo.Text=CreationHint;LastCreationError="";panel.Children.Add(_creationInfo);
        var flyout=new Flyout{Content=panel,Placement=FlyoutPlacementMode.Bottom};
        _popupSearch=search;_searchPanel=panel;_canvasSearch=flyout;
        bool Current()=>!_valueEditorDisposed&&ReferenceEquals(_canvasSearch,flyout)&&ReferenceEquals(_popupSearch,search);
        // Observe the actual open dependency property; Uno can defer Opened
        // until dispatcher idle. Old popup callbacks must not affect a new one.
        flyout.RegisterPropertyChangedCallback(FlyoutBase.IsOpenProperty,(_,_)=>{
            if(Current())SearchIsOpen=flyout.IsOpen;
        });
        panel.Loaded+=(_,_)=>DispatcherQueue.TryEnqueue(()=>{
            if(Current()&&flyout.IsOpen&&search.IsLoaded)search.Focus(FocusState.Programmatic);
        });
        flyout.Closed+=(_,_)=>{
            search.IsSuggestionListOpen=false;
            if(Current())SearchIsOpen=flyout.IsOpen;
        };
        search.TextChanged+=(_,_)=>{
            if(!Current()||!flyout.IsOpen||!ReferenceEquals(Session,session)||!ReferenceEquals(session.Graph,graph))return;
            search.ItemsSource=_canvasSearchIndex.Search(graph.Registry,search.Text);
            _creationInfo.Text=CreationHint;LastCreationError="";
        };
        search.QuerySubmitted+=(_,e)=>{
            if(!Current()||!flyout.IsOpen)return;
            if(!ReferenceEquals(Session,session)||!ReferenceEquals(session.Graph,graph)){
                CreationError("Definition changed. Reopen search.");return;
            }
            // QueryText is captured by the native editor at the Enter event.
            // The outer Text property can still lag immediate typing or paste.
            var query=e.ChosenSuggestion?.ToString()??e.QueryText;
            if(CreateFromQuery(query,insertion)){search.IsSuggestionListOpen=false;flyout.Hide();}
        };
        var center=Canvas.ViewCenter;
        var x=Canvas.ActualWidth/2+(insertion.X-center.X)*Canvas.Zoom;
        var y=Canvas.ActualHeight/2+(insertion.Y-center.Y)*Canvas.Zoom;
        var origin=Canvas.TransformToVisual(XamlRoot.Content).TransformPoint(new(0,0));
        // This is the centered popup anchor, not its left edge. Clamping the
        // visual anchor never changes the captured graph insertion coordinates.
        var halfWidth=(search.Width+48)/2;
        var minimumX=Math.Min(rootWidth/2,halfWidth+8);
        var maximumX=Math.Max(minimumX,rootWidth-halfWidth-8);
        var anchorX=Math.Clamp(origin.X+x,minimumX,maximumX);
        var anchorY=Math.Clamp(origin.Y+y,8,Math.Max(8,rootHeight-132));
        flyout.ShowAt(Canvas,new FlyoutShowOptions{Position=new Point(anchorX-origin.X,anchorY-origin.Y)});
    }
}
