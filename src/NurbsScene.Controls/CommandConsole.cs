using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NurbsScene.Presentation;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace NurbsScene.Controls;

/// <summary>Native inline command input and selectable history with complete visual lines.</summary>
public sealed class CommandConsole : Grid
{
    private const double CommandFontSize = 12;
    private const double HistoryLineHeight = 16;
    private const double EntryHeight = 20;
    private const int CompactLines = 2;
    private const int ExpandedLines = 13;
    private const int MaximumHistoryLines = 500;

    private readonly AutoSuggestBox _input = new()
    {
        FontSize = CommandFontSize, FontWeight = FontWeights.Normal,
        MinWidth = 0, MinHeight = 0, Height = EntryHeight,
        Padding = new(0), BorderThickness = new(0), CornerRadius = new(0),
        Background = ClassicChrome.Surface, Foreground = ClassicChrome.Ink,
        VerticalContentAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _log = new()
    {
        FontSize = CommandFontSize, FontWeight = FontWeights.Normal,
        LineHeight = HistoryLineHeight, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        TextWrapping = TextWrapping.Wrap, Padding = new(0),
        Foreground = ClassicChrome.Ink, IsTextSelectionEnabled = true,
        HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top
    };
    private readonly ScrollViewer _historyView = new()
    {
        Padding = new(0), BorderThickness = new(0), MinHeight = 0,
        HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollMode = ScrollMode.Enabled, VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
        IsHorizontalRailEnabled = false, IsVerticalRailEnabled = true,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Top,
        Background = ClassicChrome.Surface
    };
    private readonly TextBlock _prompt = new()
    {
        Text = "Command:", FontSize = CommandFontSize, FontWeight = FontWeights.SemiBold,
        Foreground = ClassicChrome.Ink, VerticalAlignment = VerticalAlignment.Center,
        MaxWidth = 230, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(5,0,3,0)
    };
    private readonly TextBlock _historyArrow = new()
    {
        Text = "\u25be", FontSize = 12, FontWeight = FontWeights.Normal,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };
    private readonly CommandRecall _history = new();
    private readonly Queue<string> _lines = new();
    private readonly Button _expand;
    private string _historyText = "";
    private string? _repeatCommand;
    private bool _setting, _canvasFocusQueued, _historyUpdateQueued, _snapQueued;
    private bool _followTail = true, _selectingHistory, _changingScroll, _applyingHistoryLayout;
    private double _historyViewportHeight = double.NaN, _historyExtentHeight = double.NaN;
    private long _layoutGeneration;
    private long? _historySelectionToken;

    public event Action<string>? CommandSubmitted;
    public event Action<bool>? HistoryExpandedRequested;
    public FrameworkElement InputElement => _input;
    public int HistoryCount => _history.Count;
    public bool HistoryExpanded { get; private set; }
    public string InputText => _input.Text;
    /// <summary>Whether canvas text is awaiting the deferred native command-input focus transfer.</summary>
    public bool IsCanvasFocusPending => _canvasFocusQueued;
    /// <summary>A canvas-entered draft waiting for native command-input focus.</summary>
    public bool HasPendingCanvasCommand => _canvasFocusQueued && _input.Text.Length > 0;
    public string HistoryText => _historyText;

    public CommandConsole()
    {
        Background = ClassicChrome.Surface;
        BorderBrush = ClassicChrome.Border;
        BorderThickness = new(0,0,0,1);
        RowDefinitions.Add(new() { Height = new GridLength(CompactLines * HistoryLineHeight) });
        RowDefinitions.Add(new() { Height = new GridLength(EntryHeight + 1) });

        // Padding is horizontal only: the history viewport is exactly N native
        // text lines. Wrapped results never introduce a horizontal scrollbar that
        // steals part of a row from this compact command band.
        _log.Margin = new(5,0,0,0);
        _historyView.Content = _log;
        Children.Add(_historyView);

        ConfigureInputStyle();
        var row = new Grid { Background = ClassicChrome.Surface };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1,GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(18) });
        row.Children.Add(_prompt);
        row.Children.Add(_input);
        SetColumn(_input,1);
        _expand = ClassicChrome.Button("Expand command history", () => HistoryExpandedRequested?.Invoke(!HistoryExpanded), "ToggleCommandHistory");
        _expand.Width = 18;
        _expand.Height = EntryHeight;
        _expand.Padding = new(0);
        _expand.BorderThickness = new(1,0,0,0);
        _expand.Background = ClassicChrome.Surface;
        _expand.Content = _historyArrow;
        ToolTipService.SetToolTip(_expand,"Expand command history (F2)");
        row.Children.Add(_expand);
        SetColumn(_expand,2);
        var entry = new Border
        {
            Background = ClassicChrome.Surface, BorderBrush = ClassicChrome.Border,
            BorderThickness = new(0,1,0,0), Child = row
        };
        Children.Add(entry);
        SetRow(entry,1);

        AutomationProperties.SetAutomationId(_input,"CommandInput");
        AutomationProperties.SetName(_input,"Command");
        AutomationProperties.SetAutomationId(_historyView,"CommandHistory");
        AutomationProperties.SetName(_historyView,"Command history");
        AutomationProperties.SetAutomationId(_log,"CommandHistoryText");
        AutomationProperties.SetName(_log,"Command history text");
        AutomationProperties.SetAutomationId(_prompt,"CommandPrompt");
        AutomationProperties.SetAutomationId(row,"CommandEntryRow");

        ConfigureHistory();
        _input.TextChanged += (_,_) =>
        {
            if (_setting) return;
            var text = _input.Text.Trim();
            _input.ItemsSource = text.Length > 0 && !text.Contains(' ')
                ? WorkspaceShellCommands.Search(text).Concat(WorkspaceCommands.Search(text).Select(t => t.Command))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray()
                : Array.Empty<string>();
        };
        _input.QuerySubmitted += (_,e) =>
        {
            Submit(e.ChosenSuggestion?.ToString() ?? e.QueryText);
            // AutoSuggestBox restores QueryText after QuerySubmitted on some Uno
            // hosts. Clear after that framework update, not only inside Submit.
            DispatcherQueue.TryEnqueue(() => { SetText(""); _input.IsSuggestionListOpen = false; });
        };
        _input.KeyDown += (_,e) =>
        {
            if (e.Key == VirtualKey.Escape)
            {
                SetText(""); _history.ResetNavigation(); _input.IsSuggestionListOpen = false;
                CommandSubmitted?.Invoke("Cancel"); e.Handled = true;
            }
            else if (!_input.IsSuggestionListOpen && e.Key == VirtualKey.Up)
            {
                SetText(_history.Previous(_input.Text)); CaretAtEnd(); e.Handled = true;
            }
            else if (!_input.IsSuggestionListOpen && e.Key == VirtualKey.Down)
            {
                SetText(_history.Next()); CaretAtEnd(); e.Handled = true;
            }
        };
        _input.GotFocus += (_,_) => QueueHistoryUpdate(force: true);
        Loaded += (_,_) =>
        {
            _layoutGeneration++;
            _historySelectionToken ??= _log.RegisterPropertyChangedCallback(TextBlock.SelectedTextProperty,(_,_) =>
            {
                if (_log.SelectedText.Length == 0) QueueHistoryUpdate();
            });
            QueueHistoryUpdate();
        };
        Unloaded += (_,_) =>
        {
            _layoutGeneration++;
            _historyUpdateQueued = _snapQueued = _selectingHistory = false;
            if (_historySelectionToken is { } token)
            {
                _log.UnregisterPropertyChangedCallback(TextBlock.SelectedTextProperty,token);
                _historySelectionToken = null;
            }
        };
    }

    private void ConfigureInputStyle()
    {
        // AutoSuggestBox's inner native TextBox owns its padding, minimum height
        // and focused border. Styling only the outer control leaves a clipped,
        // vertically displaced input inside an apparently compact row.
        var style = new Style { TargetType = typeof(TextBox) };
        if (Application.Current.Resources.TryGetValue("AutoSuggestBoxTextBoxStyle",out var nativeStyle) && nativeStyle is Style basis)
            style.BasedOn = basis;
        style.Setters.Add(new Setter(Control.FontSizeProperty,CommandFontSize));
        style.Setters.Add(new Setter(Control.FontWeightProperty,FontWeights.Normal));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty,0d));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,0d));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty,EntryHeight));
        style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty,new CornerRadius(0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty,ClassicChrome.Surface));
        style.Setters.Add(new Setter(Control.ForegroundProperty,ClassicChrome.Ink));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty,VerticalAlignment.Center));
        _input.TextBoxStyle = style;
        foreach (var state in new[] { "", "PointerOver", "Focused" })
        {
            _input.Resources["TextControlBackground" + state] = ClassicChrome.Surface;
            _input.Resources["TextControlForeground" + state] = ClassicChrome.Ink;
            _input.Resources["TextControlBorderBrush" + state] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        _input.Resources["TextControlThemeMinHeight"] = 0d;
        _input.Resources["TextControlThemeMinWidth"] = 0d;
        _input.Resources["TextControlThemePadding"] = new Thickness(0);
        _input.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
        _input.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        _input.Resources["AutoSuggestBoxIconFontSize"] = 12d;
        _input.Resources["AutoSuggestBoxInnerButtonMargin"] = new Thickness(0);
        _input.Resources["AutoSuggestBoxDeleteButtonMargin"] = new Thickness(0);
    }

    private void ConfigureHistory()
    {
        _log.IsTabStop = true;
        _log.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,_) =>
        {
            _selectingHistory = true;
            _log.Focus(FocusState.Pointer);
        }),true);
        void EndSelection(object sender,PointerRoutedEventArgs e)
        {
            _selectingHistory = false;
            QueueHistoryUpdate();
            QueueHistorySnap();
        }
        _log.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler(EndSelection),true);
        _log.AddHandler(UIElement.PointerCanceledEvent,new PointerEventHandler(EndSelection),true);
        _log.AddHandler(UIElement.PointerCaptureLostEvent,new PointerEventHandler(EndSelection),true);
        _historyView.AddHandler(UIElement.PointerWheelChangedEvent,new PointerEventHandler((_,e) =>
        {
            var wheel = e.GetCurrentPoint(_historyView).Properties;
            if (e.KeyModifiers != VirtualKeyModifiers.None || wheel.IsHorizontalMouseWheel) return;
            // Native wheel motion starts on the next frame. Record intent now
            // so a pending append cannot cancel it before the first ViewChanged.
            // Leave native handling and edge chaining entirely untouched.
            if ((wheel.MouseWheelDelta > 0 && _historyView.VerticalOffset > 0)
                || (wheel.MouseWheelDelta < 0 && _historyView.VerticalOffset < _historyView.ScrollableHeight))
                _followTail = false;
        }),true);
        _historyView.SizeChanged += (_,_) => QueueHistoryUpdate();
        _historyView.ViewChanged += (_,e) =>
        {
            var viewportHeight = _historyView.ViewportHeight;
            var extentHeight = _historyView.ExtentHeight;
            var layoutChanged = !double.IsFinite(_historyViewportHeight)
                || Math.Abs(viewportHeight - _historyViewportHeight) > .1
                || Math.Abs(extentHeight - _historyExtentHeight) > .1;
            _historyViewportHeight = viewportHeight;
            _historyExtentHeight = extentHeight;
            if (_changingScroll || _applyingHistoryLayout) return;
            // Extent changes and F2 resizing preserve the reader's intent. A
            // real scroll still changes it while an append is merely queued.
            if (!layoutChanged) _followTail = IsAtTail;
            if (!e.IsIntermediate) QueueHistorySnap();
        };

        var menu = new MenuFlyout();
        MenuFlyoutItem Item(string text,Action action)
        {
            var item = new MenuFlyoutItem { Text = text, FontSize = 11, MinHeight = 23, Padding = new(7,2,7,2) };
            item.Click += (_,_) => { try { action(); } catch (Exception error) { WriteLine("Clipboard: " + error.Message); } };
            menu.Items.Add(item); return item;
        }
        var copy = Item("Copy",_log.CopySelectionToClipboard);
        Item("Copy all",() => { var data = new DataPackage(); data.SetText(_historyText); Clipboard.SetContent(data); });
        Item("Select all",() => { _log.Focus(FocusState.Programmatic); _log.SelectAll(); });
        menu.Items.Add(new MenuFlyoutSeparator());
        Item("Clear history",ClearHistory);
        menu.Opening += (_,_) => copy.IsEnabled = _log.SelectedText.Length > 0;
        _log.ContextFlyout = menu;
        _historyView.ContextFlyout = menu;
    }

    private bool IsAtTail => _historyView.ScrollableHeight - _historyView.VerticalOffset <= 1;

    private void QueueHistoryUpdate(bool force = false)
    {
        if (force)
        {
            _followTail = true;
            // A new command deliberately resumes the live transcript. Ordinary
            // asynchronous output must not reset a reader's native selection.
            if (_log.SelectedText.Length > 0) _log.Text = "";
            if (_log.Text != _historyText) _log.Text = _historyText;
        }
        if (_historyUpdateQueued || !IsLoaded) return;
        _historyUpdateQueued = true;
        var generation = _layoutGeneration;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (generation != _layoutGeneration || !IsLoaded) return;
            try
            {
                if (_selectingHistory || _log.SelectedText.Length > 0) return;
                _applyingHistoryLayout = true;
                if (_log.Text != _historyText) _log.Text = _historyText;
                _historyView.UpdateLayout();
                // A changed extent need not raise ViewChanged when the offset
                // stays fixed. Do not mistake the next reader scroll for layout.
                _historyViewportHeight = _historyView.ViewportHeight;
                _historyExtentHeight = _historyView.ExtentHeight;
                if (_followTail) ChangeHistoryOffset(_historyView.ScrollableHeight);
            }
            finally { _applyingHistoryLayout = _historyUpdateQueued = false; }
        })) _historyUpdateQueued = false;
    }

    private void QueueHistorySnap()
    {
        if (_snapQueued || _selectingHistory || !IsLoaded) return;
        _snapQueued = true;
        var generation = _layoutGeneration;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (generation != _layoutGeneration || !IsLoaded) return;
            _snapQueued = false;
            if (!_selectingHistory) ChangeHistoryOffset(_historyView.VerticalOffset);
        })) _snapQueued = false;
    }

    private void ChangeHistoryOffset(double offset)
    {
        // Rasterization may round the native extent by a fraction of a DIP.
        // Keep the true tail reachable while snapping intermediate positions to
        // visual lines, including wrapped command results.
        var target = Math.Clamp(Math.Round(offset / HistoryLineHeight) * HistoryLineHeight,0,_historyView.ScrollableHeight);
        if (Math.Abs(target - _historyView.VerticalOffset) < .1) return;
        _changingScroll = true;
        try { _historyView.ChangeView(null,target,null,disableAnimation: true); }
        finally { _changingScroll = false; }
    }

    public void SetHistoryExpanded(bool value)
    {
        if (HistoryExpanded == value) return;
        HistoryExpanded = value;
        RowDefinitions[0].Height = new GridLength((value ? ExpandedLines : CompactLines) * HistoryLineHeight);
        _historyArrow.Text = value ? "\u25b4" : "\u25be";
        AutomationProperties.SetName(_expand,value ? "Collapse command history" : "Expand command history");
        ToolTipService.SetToolTip(_expand,value ? "Collapse command history (F2)" : "Expand command history (F2)");
        QueueHistoryUpdate();
    }

    private void SetText(string text)
    {
        _setting = true;
        try { _input.Text = text; _input.ItemsSource = Array.Empty<string>(); }
        finally { _setting = false; }
    }

    private void Submit(string input)
    {
        try
        {
            var command = string.IsNullOrWhiteSpace(input) ? _repeatCommand : _history.Submit(input);
            SetText(""); _input.IsSuggestionListOpen = false;
            if (command is not null) CommandSubmitted?.Invoke(command);
        }
        catch (Exception error) { WriteLine(error.Message); }
    }

    /// <summary>Submits a canvas draft when Enter arrives before its queued focus transfer.</summary>
    public bool TrySubmitPendingCanvasCommand()
    {
        if (!HasPendingCanvasCommand) return false;
        Submit(_input.Text); return true;
    }

    public void Repeat() => Submit("");
    public void Remember(string command,bool repeatable = true)
    {
        if (string.Equals(command,"Cancel",StringComparison.OrdinalIgnoreCase)) return;
        _history.Submit(command);
        if (repeatable) _repeatCommand = command;
    }

    public void SetPrompt(string pending)
    {
        _prompt.Text = string.IsNullOrEmpty(pending) ? "Command:" : pending + ":";
        ToolTipService.SetToolTip(_prompt,_prompt.Text);
    }

    public void ClearHistory()
    {
        _lines.Clear(); _historyText = ""; _log.Text = ""; _followTail = true;
        QueueHistoryUpdate();
    }

    public void WriteLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var command = text.StartsWith("> ",StringComparison.Ordinal);
        if (command) text = "Command: " + text[2..];
        if (text.Length > 4096)
        {
            var length = char.IsHighSurrogate(text[4095]) ? 4095 : 4096;
            text = text[..length] + "…";
        }
        foreach (var line in text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n').Split('\n'))
            _lines.Enqueue(line);
        while (_lines.Count > MaximumHistoryLines) _lines.Dequeue();
        _historyText = string.Join('\n',_lines);
        QueueHistoryUpdate(force: command);
    }

    private void CaretAtEnd()
    {
        TextBox? Find(DependencyObject element)
        {
            if (element is TextBox box) return box;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                if (Find(VisualTreeHelper.GetChild(element,i)) is { } found) return found;
            return null;
        }
        Find(_input)?.Select(_input.Text.Length,0);
    }

    public void BeginCommand(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 1)
        {
            // Consume canvas characters exactly once before transferring focus
            // after the current native character dispatch has completed.
            SetText(_input.Text + text);
            if (!_canvasFocusQueued)
            {
                _canvasFocusQueued = true;
                DispatcherQueue.TryEnqueue(() => { _canvasFocusQueued = false; _input.Focus(FocusState.Programmatic); CaretAtEnd(); });
            }
            return;
        }
        SetText(text); _input.Focus(FocusState.Programmatic); CaretAtEnd();
    }
}
