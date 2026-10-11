using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Presentation;
using Windows.System;

namespace NurbsScene.Controls;

public sealed class CommandConsole : Grid
{
    private readonly AutoSuggestBox _input=new(){PlaceholderText="Enter a command",FontSize=12,MinHeight=25,Padding=new(0),BorderThickness=new(0)};
    private readonly TextBox _log=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.NoWrap,FontSize=11,BorderThickness=new(0),Padding=new(5,0,5,0),MinHeight=30,Background=ClassicChrome.Brush(255,255,255)};
    private readonly CommandRecall _history=new();
    private readonly Queue<string> _lines=new();private bool _setting;
    private readonly TextBlock _prompt=ClassicChrome.Label("Command:",12);
    public event Action<string>? CommandSubmitted;
    public FrameworkElement InputElement=>_input;
    public int HistoryCount=>_history.Count;
    public CommandConsole()
    {
        Background=ClassicChrome.Brush(255,255,255);
        RowDefinitions.Add(new(){Height=new GridLength(38)});RowDefinitions.Add(new(){Height=new GridLength(27)});
        Children.Add(_log);var row=new Grid();row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        _prompt.Margin=new(5,0,4,0);row.Children.Add(_prompt);row.Children.Add(_input);SetColumn(_input,1);Children.Add(row);SetRow(row,1);
        AutomationProperties.SetAutomationId(_input,"CommandInput");AutomationProperties.SetName(_input,"Command");AutomationProperties.SetAutomationId(_log,"CommandHistory");
        _input.TextChanged+=(_,e)=>{if(_setting)return;var text=_input.Text.Trim();_input.ItemsSource=text.Length>0&&!text.Contains(' ')?WorkspaceCommands.Search(text).Select(t=>t.Command).ToArray():Array.Empty<string>();};
        _input.QuerySubmitted+=(_,e)=>Submit(e.ChosenSuggestion?.ToString()??_input.Text);
        _input.KeyDown+=(_,e)=>{
            if(e.Key==VirtualKey.Escape){SetText("");_history.ResetNavigation();_input.IsSuggestionListOpen=false;CommandSubmitted?.Invoke("Cancel");e.Handled=true;}
            else if(!_input.IsSuggestionListOpen&&e.Key==VirtualKey.Up){SetText(_history.Previous(_input.Text));e.Handled=true;}
            else if(!_input.IsSuggestionListOpen&&e.Key==VirtualKey.Down){SetText(_history.Next());e.Handled=true;}
        };
    }
    private void SetText(string text){_setting=true;try{_input.Text=text;_input.ItemsSource=Array.Empty<string>();}finally{_setting=false;}}
    private void Submit(string input)
    {
        try{var command=_history.Submit(input);SetText("");_input.IsSuggestionListOpen=false;if(command is not null)CommandSubmitted?.Invoke(command);}
        catch(Exception error){WriteLine(error.Message);}
    }
    public void SetPrompt(string pending)=>_prompt.Text=string.IsNullOrEmpty(pending)?"Command:":pending+":";
    public void ClearHistory(){_lines.Clear();_log.Text="";}
    public void WriteLine(string text){_lines.Enqueue(text.Length>4096?text[..4096]+"…":text);while(_lines.Count>150)_lines.Dequeue();_log.Text=string.Join('\n',_lines);_log.SelectionStart=_log.Text.Length;}
    public void BeginCommand(string text){SetText(text);_input.Focus(FocusState.Programmatic);}
}
