using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace NurbsScene.Controls;

public sealed partial class GraphEditorControl
{
    // This is a retained native child of the canvas column, not a replacement
    // for the canvas. Its bounded rectangle leaves canvas gestures available
    // around it; short panes scroll the instructions within the card.
    private readonly Border _gettingStarted = new()
    {
        Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new(16,12,16,12),
        Width = 0,
        MaxHeight = 0,
        Background = ClassicChrome.Brush(250,250,246),
        BorderBrush = ClassicChrome.Brush(184,190,173),
        BorderThickness = new(1),
        CornerRadius = new(2)
    };

    public bool GettingStartedVisible => !_chromeDisposed && IsLoaded && !_isFolded
        && Visibility == Visibility.Visible && _gettingStarted.Visibility == Visibility.Visible
        && _gettingStarted.ActualWidth > 0 && _gettingStarted.ActualHeight > 0;

    /// <summary>Activates the existing canvas or component search after the host
    /// has shown, unfolded and laid out this editor. A live modal editor keeps
    /// its focus, and an already-open component search retains its text/session.</summary>
    public void ActivateCanvas(bool search = false)
    {
        if (_chromeDisposed || Session is null || _isFolded || Visibility != Visibility.Visible
            || XamlRoot is null || !Canvas.IsLoaded || Canvas.ActualWidth <= 0 || Canvas.ActualHeight <= 0
            || _helpDialog is not null || _textPanelDialog is not null
            || _numberSliderDialog is not null || _panelTypographyDialog is not null) return;
        // A workspace-owned ContentDialog (rendering, file confirmation, etc.)
        // also owns input even though its lifetime is not stored by this editor.
        for (var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            focused is not null; focused = VisualTreeHelper.GetParent(focused))
            if (focused is ContentDialog) return;
        Canvas.CancelInteraction();
        var existingSearch = _canvasSearch?.IsOpen == true;
        if (!existingSearch) CloseActionPopups();
        if (search || existingSearch) ShowCanvasSearch();
        else Canvas.Focus(FocusState.Programmatic);
    }

    /// <summary>Opens the existing executable component search after layout.</summary>
    public void OpenComponentSearch() => ActivateCanvas(search: true);

    private Border CreateGettingStarted()
    {
        AutomationProperties.SetAutomationId(_gettingStarted,"GraphGettingStarted");
        AutomationProperties.SetName(_gettingStarted,"Grasshopper visual programming getting started");
        var content = new StackPanel { Spacing = 8, Padding = new(12) };
        content.Children.Add(new TextBlock
        {
            Text = "Grasshopper · Visual programming",
            FontSize = 13,
            Foreground = ClassicChrome.Ink,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(Instruction("Start with a component, or insert an editable extrusion definition."));

        var actions = new StackPanel { Spacing = 5, MaxWidth = 260, HorizontalAlignment = HorizontalAlignment.Stretch };
        actions.Children.Add(StartButton("Add a component",OpenComponentSearch,"GraphStartAddComponent"));
        actions.Children.Add(StartButton("Insert extrusion example",() =>
        {
            // A second queued click must not insert a second example after the
            // first edit has already collapsed this empty-definition card.
            if (_chromeDisposed || _isFolded || Session?.Graph.NodeCount != 0) return;
            WorkspaceActionRequested?.Invoke("InsertExample");
        },"GraphStartExample"));
        content.Children.Add(actions);
        content.Children.Add(ClassicChrome.Separator(false));

        var steps = new StackPanel { Spacing = 4 };
        steps.Children.Add(Instruction("Add · Choose a palette component or double-click the canvas to search."));
        steps.Children.Add(Instruction("Connect · Drag an output port to an input port."));
        steps.Children.Add(Instruction("Adjust · Drag slider rails to update the viewport preview."));
        steps.Children.Add(Instruction("Bake · Select geometry components, then Bake to create model objects."));
        content.Children.Add(steps);
        _gettingStarted.Child = new ScrollViewer
        {
            Content = content,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BringIntoViewOnFocusChange = true
        };
        return _gettingStarted;
    }

    private static TextBlock Instruction(string text) => new()
    {
        Text = text, FontSize = 11, Foreground = ClassicChrome.Ink, TextWrapping = TextWrapping.Wrap
    };

    private static Button StartButton(string text,Action action,string id)
    {
        var button = ClassicChrome.Button(text,action,id);
        button.Content = Instruction(text);
        button.Height = double.NaN;
        button.MinHeight = 28;
        button.MinWidth = 0;
        button.Padding = new(8,4,8,4);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        return button;
    }

    private void UpdateGettingStarted()
    {
        _gettingStarted.Visibility = !_chromeDisposed && Session?.Graph.NodeCount == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SizeGettingStarted()
    {
        if (_chromeDisposed) return;
        _gettingStarted.Width = Math.Min(420,Math.Max(0,Canvas.ActualWidth - 32));
        _gettingStarted.MaxHeight = Math.Max(0,Canvas.ActualHeight - 24);
    }
}
