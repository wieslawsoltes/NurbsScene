using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NurbsScene.Presentation;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace NurbsScene.Controls;

/// <summary>Native command input and selectable, scrollable command history.</summary>
public sealed class CommandConsole : Grid
{
    private readonly AutoSuggestBox _input=new(){PlaceholderText="Enter a command",FontSize=12,MinHeight=25,Padding=new(0),BorderThickness=new(0)};
    private readonly TextBox _log=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.NoWrap,FontSize=11,BorderThickness=new(0),Padding=new(5,0,5,0),MinHeight=30,Background=ClassicChrome.Brush(255,255,255)};
    private readonly CommandRecall _history=new();
    private readonly Queue<string> _lines=new();
    private readonly TextBlock _prompt=ClassicChrome.Label("Command:",12);
    private readonly Button _expand;
    private bool _setting;
    private string? _repeatCommand;
    public event Action<string>? CommandSubmitted;
    public event Action<bool>? HistoryExpandedRequested;
    public FrameworkElement InputElement=>_input;
    public int HistoryCount=>_history.Count;
    public bool HistoryExpanded {get;private set;}
    public string InputText=>_input.Text;
    public string HistoryText=>_log.Text;
    public CommandConsole()
    {
        Background=ClassicChrome.Brush(255,255,255);
        RowDefinitions.Add(new(){Height=new GridLength(38)});RowDefinitions.Add(new(){Height=new GridLength(27)});
        ScrollViewer.SetVerticalScrollBarVisibility(_log,ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(_log,ScrollBarVisibility.Auto);
        Children.Add(_log);
        var row=new Grid();row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        _prompt.Margin=new(5,0,4,0);row.Children.Add(_prompt);row.Children.Add(_input);SetColumn(_input,1);
        _expand=ClassicChrome.Button("History ▾",()=>HistoryExpandedRequested?.Invoke(!HistoryExpanded),"ToggleCommandHistory");
        ToolTipService.SetToolTip(_expand,"Command history (F2)");row.Children.Add(_expand);SetColumn(_expand,2);
        Children.Add(row);SetRow(row,1);
        AutomationProperties.SetAutomationId(_input,"CommandInput");AutomationProperties.SetName(_input,"Command");AutomationProperties.SetAutomationId(_log,"CommandHistory");
        // A read-only WinUI/Uno TextBox does not consistently take focus when
        // its empty scrollable area is pressed. Observe handled child presses as
        // well, so selection/copy works across the complete history surface.
        _log.IsTabStop=true;_log.AllowFocusOnInteraction=true;
        _log.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,_)=>_log.Focus(FocusState.Pointer)),true);
        var menu=new MenuFlyout();
        var copy=new MenuFlyoutItem{Text="Copy history"};copy.Click+=(_,_)=>{
            try{var data=new DataPackage();data.SetText(_log.Text);Clipboard.SetContent(data);}catch(Exception error){WriteLine("Clipboard: "+error.Message);}
        };
        var clear=new MenuFlyoutItem{Text="Clear history"};clear.Click+=(_,_)=>ClearHistory();
        menu.Items.Add(copy);menu.Items.Add(clear);_log.ContextFlyout=menu;
        _input.TextChanged+=(_,_)=>{
            if(_setting)return;var text=_input.Text.Trim();
            _input.ItemsSource=text.Length>0&&!text.Contains(' ')?WorkspaceShellCommands.Search(text)
                .Concat(WorkspaceCommands.Search(text).Select(t=>t.Command)).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray():Array.Empty<string>();
        };
        _input.QuerySubmitted+=(_,e)=>{
            Submit(e.ChosenSuggestion?.ToString()??e.QueryText);
            // AutoSuggestBox restores QueryText after QuerySubmitted on some Uno
            // hosts. Clear after that framework update, not only inside Submit.
            DispatcherQueue.TryEnqueue(()=>{SetText("");_input.IsSuggestionListOpen=false;});
        };
        _input.KeyDown+=(_,e)=>{
            if(e.Key==VirtualKey.Escape){SetText("");_history.ResetNavigation();_input.IsSuggestionListOpen=false;CommandSubmitted?.Invoke("Cancel");e.Handled=true;}
            else if(!_input.IsSuggestionListOpen&&e.Key==VirtualKey.Up){SetText(_history.Previous(_input.Text));CaretAtEnd();e.Handled=true;}
            else if(!_input.IsSuggestionListOpen&&e.Key==VirtualKey.Down){SetText(_history.Next());CaretAtEnd();e.Handled=true;}
        };
    }
    public void SetHistoryExpanded(bool value)
    {
        HistoryExpanded=value;RowDefinitions[0].Height=new GridLength(value?220:38);
        _expand.Content=value?"History ▴":"History ▾";
        AutomationProperties.SetName(_expand,value?"Collapse command history":"Expand command history");
    }
    private void SetText(string text){_setting=true;try{_input.Text=text;_input.ItemsSource=Array.Empty<string>();}finally{_setting=false;}}
    private void Submit(string input)
    {
        try{var command=string.IsNullOrWhiteSpace(input)?_repeatCommand:_history.Submit(input);SetText("");_input.IsSuggestionListOpen=false;if(command is not null)CommandSubmitted?.Invoke(command);}
        catch(Exception error){WriteLine(error.Message);}
    }
    public void Repeat()=>Submit("");
    public void Remember(string command,bool repeatable=true){if(string.Equals(command,"Cancel",StringComparison.OrdinalIgnoreCase))return;_history.Submit(command);if(repeatable)_repeatCommand=command;}
    public void SetPrompt(string pending)=>_prompt.Text=string.IsNullOrEmpty(pending)?"Command:":pending+":";
    public void ClearHistory(){_lines.Clear();_log.Text="";}
    public void WriteLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if(text.Length>4096){var length=char.IsHighSurrogate(text[4095])?4095:4096;text=text[..length]+"…";}
        _lines.Enqueue(text);while(_lines.Count>150)_lines.Dequeue();_log.Text=string.Join('\n',_lines);_log.SelectionStart=_log.Text.Length;
    }
    private void CaretAtEnd()
    {
        TextBox? Find(DependencyObject element)
        {
            if(element is TextBox box)return box;
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(element);i++)if(Find(VisualTreeHelper.GetChild(element,i)) is{} found)return found;
            return null;
        }
        Find(_input)?.Select(_input.Text.Length,0);
    }
    public void BeginCommand(string text){SetText(text);_input.Focus(FocusState.Programmatic);CaretAtEnd();}
}
