using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;
using NurbsScene.Presentation;
using NurbsScene.Parametric;

namespace NurbsScene.Controls;

public sealed partial class GraphEditorControl
{
    private StackPanel? _searchPanel;
    private Graph? _searchOwner;
    private CanvasPoint _searchInsertion;
    private readonly CanvasSearchIndex _canvasSearchIndex=new();
    private void ShowCanvasSearch()
    {
        if(Session is null||XamlRoot is null)return;
        if(_canvasSearch is null)
        {
            _popupSearch=new AutoSuggestBox{PlaceholderText="Search components…",Width=290,FontSize=12,MaxSuggestionListHeight=180};
            AutomationProperties.SetAutomationId(_popupSearch,"CanvasComponentSearch");
            _searchPanel=new StackPanel{Spacing=5};AutomationProperties.SetAutomationId(_searchPanel,"CanvasSearchPanel");
            _searchPanel.Children.Add(ClassicChrome.Label("Search",11));_searchPanel.Children.Add(_popupSearch);
            _searchPanel.Children.Add(_creationInfo);
            _canvasSearch=new Flyout{Content=_searchPanel,Placement=FlyoutPlacementMode.Bottom};
            // Uno 6.7.135 defers FlyoutBase.Opened to dispatcher Idle. A rendered
            // flyout can therefore be open before that event is delivered. Read
            // the actual dependency property, not an event-derived shadow state.
            // Reference: Uno.UI/UI/Xaml/Controls/Flyout/FlyoutBase.cs, ShowAtCore.
            _canvasSearch.RegisterPropertyChangedCallback(FlyoutBase.IsOpenProperty,
                (sender,_)=>SearchIsOpen=((FlyoutBase)sender).IsOpen);
            _searchPanel.Loaded+=(_,_)=>DispatcherQueue.TryEnqueue(()=>{
                if(_canvasSearch.IsOpen&&_popupSearch.IsLoaded)_popupSearch.Focus(FocusState.Programmatic);
            });
            _canvasSearch.Closed+=(_,_)=>{
                SearchIsOpen=_canvasSearch.IsOpen;
                if(!SearchIsOpen)_popupSearch.IsSuggestionListOpen=false;
            };
            _popupSearch.TextChanged+=(_,_)=>{
                if(Session is null)return;
                _popupSearch.ItemsSource=_canvasSearchIndex.Search(Session.Graph.Registry,_popupSearch.Text);
                _creationInfo.Text=CreationHint;LastCreationError="";
            };
            _popupSearch.QuerySubmitted+=(_,e)=>{
                var query=e.ChosenSuggestion?.ToString()??_popupSearch.Text;
                if(!ReferenceEquals(Session?.Graph,_searchOwner)){CreationError("Definition changed. Reopen search.");return;}
                if(CreateFromQuery(query,_searchInsertion)){_popupSearch.IsSuggestionListOpen=false;_canvasSearch.Hide();}
            };
        }
        _popupSearch!.Text="";_popupSearch.ItemsSource=Array.Empty<string>();_popupSearch.IsSuggestionListOpen=false;
        _searchOwner=Session.Graph;_searchInsertion=Canvas.InsertionPoint;
        var at=_searchInsertion;var center=Canvas.ViewCenter;
        var x=Canvas.ActualWidth/2+(at.X-center.X)*Canvas.Zoom;var y=Canvas.ActualHeight/2+(at.Y-center.Y)*Canvas.Zoom;
        var origin=Canvas.TransformToVisual(XamlRoot.Content).TransformPoint(new(0,0));
        var rootWidth=XamlRoot.Size.Width;var rootHeight=XamlRoot.Size.Height;
        if(rootWidth<=0||rootHeight<=0)return;
        // The point anchors a centered flyout, not its left edge. Bound the
        // anchor in root coordinates, allowing for presenter padding, so a
        // cursor near any canvas edge cannot put the search field offscreen.
        // Moving the popup never changes the graph's insertion coordinates.
        _popupSearch.Width=Math.Max(1,Math.Min(290,rootWidth-64));
        var halfWidth=(_popupSearch.Width+48)/2;
        var minimumX=Math.Min(rootWidth/2,halfWidth+8);
        var maximumX=Math.Max(minimumX,rootWidth-halfWidth-8);
        var anchorX=Math.Clamp(origin.X+x,minimumX,maximumX);
        var anchorY=Math.Clamp(origin.Y+y,8,Math.Max(8,rootHeight-132));
        _canvasSearch.ShowAt(Canvas,new FlyoutShowOptions{Position=new Point(anchorX-origin.X,anchorY-origin.Y)});
    }
}
