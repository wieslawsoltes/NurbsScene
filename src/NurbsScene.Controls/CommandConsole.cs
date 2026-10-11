using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace NurbsScene.Controls;

public sealed class CommandConsole : Grid
{
    private readonly TextBox _input=new(){PlaceholderText="Command: Line, Circle 0,0,0 10, Extrude 0,0,10…",FontSize=12,MinHeight=26,Padding=new(5,2,5,2),BorderThickness=new(0)};
    private readonly TextBox _log=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.NoWrap,FontSize=11,BorderThickness=new(0),Padding=new(5,2,5,2),MinHeight=45};
    private readonly List<string> _history=[];private int _cursor;private readonly Queue<string> _lines=new();
    public event Action<string>? CommandSubmitted;
    public CommandConsole()
    {
        RowDefinitions.Add(new(){Height=new GridLength(48)});RowDefinitions.Add(new(){Height=GridLength.Auto});Children.Add(_log);Children.Add(_input);SetRow(_input,1);
        AutomationProperties.SetAutomationId(_input,"CommandInput");AutomationProperties.SetName(_input,"Command");AutomationProperties.SetAutomationId(_log,"CommandHistory");
        _input.KeyDown+=(_,e)=>{switch(e.Key){case VirtualKey.Enter:var text=_input.Text.Trim();if(text.Length==0&&_history.Count>0)text=_history[^1];if(text.Length>0){_history.Add(text);_cursor=_history.Count;_input.Text="";CommandSubmitted?.Invoke(text);}e.Handled=true;break;case VirtualKey.Up:if(_history.Count>0){_cursor=Math.Max(0,_cursor-1);_input.Text=_history[_cursor];_input.SelectionStart=_input.Text.Length;}e.Handled=true;break;case VirtualKey.Down:_cursor=Math.Min(_history.Count,_cursor+1);_input.Text=_cursor<_history.Count?_history[_cursor]:"";e.Handled=true;break;case VirtualKey.Escape:_input.Text="";CommandSubmitted?.Invoke("Cancel");e.Handled=true;break;}};
    }
    public void WriteLine(string text){_lines.Enqueue(text);while(_lines.Count>150)_lines.Dequeue();_log.Text=string.Join('\n',_lines);_log.SelectionStart=_log.Text.Length;}
    public void BeginCommand(string text){_input.Text=text;_input.SelectionStart=text.Length;_input.Focus(FocusState.Programmatic);}
}
