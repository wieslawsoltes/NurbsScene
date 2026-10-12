using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Parametric;
using NurbsScene.Presentation;

namespace NurbsScene.Controls;

/// <summary>Reusable draft-only persistent text editor. Connected data remains
/// authoritative until its wire is disconnected; Cancel never edits a graph.</summary>
public sealed class TextPanelDialog : ContentDialog
{
    private readonly TextBox _text;
    private readonly PanelTextDraft _draft;
    private readonly CheckBox _single;
    public string DraftText => _draft.Resolve(_text.Text);
    public bool SingleItem => _single.IsChecked == true;
    public TextPanelDialog(GraphNode node, bool connected)
    {
        Title = "Text Panel"; PrimaryButtonText = "Apply"; CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.None;
        AutomationProperties.SetAutomationId(this, "TextPanelDialog");
        var original = node.Parameters.GetValueOrDefault("text", node.Component == "Text" ? "NurbsScene" : "") is string text
            ? text : throw new ArgumentException("Persistent panel text must be a string.");
        _draft = new(original);
        // Text is coerced while AcceptsReturn is false. Enable multiline first,
        // otherwise reopening a saved draft truncates it at the first newline.
        _text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            Height = 200, MaxLength = InputComponents.MaximumTextLength, FontSize = 12,
            Text = original };
        AutomationProperties.SetAutomationId(_text, "PanelTextInput");
        AutomationProperties.SetName(_text, "Persistent panel text");
        _single = new CheckBox { Content = "Keep all lines as one text item", IsChecked = node.Component == "Text" || node.Parameters.GetValueOrDefault("singleItem") is true,
            IsEnabled = node.Component != "Text", FontSize = 12 };
        AutomationProperties.SetAutomationId(_single, "PanelSingleItem");
        var body = new StackPanel { Width = 390, Spacing = 8 };
        body.Children.Add(new TextBlock { Text = connected ? "Connected input overrides this stored text. Disconnect to use the stored text." : "Each line becomes an item unless single-item mode is selected.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11 });
        body.Children.Add(_text); body.Children.Add(_single); Content = body;
    }
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PrimaryButton") is FrameworkElement apply) AutomationProperties.SetAutomationId(apply, "PanelTextApply");
        if (GetTemplateChild("CloseButton") is FrameworkElement cancel) AutomationProperties.SetAutomationId(cancel, "PanelTextCancel");
    }
}
