using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Parametric;

namespace NurbsScene.Controls;

/// <summary>Reusable draft-only persistent text editor. Connected data remains
/// authoritative until its wire is disconnected; Cancel never edits a graph.</summary>
public sealed class TextPanelDialog : ContentDialog
{
    private readonly TextBox _text;
    private readonly CheckBox _single;
    public string DraftText => _text.Text;
    public bool SingleItem => _single.IsChecked == true;
    public TextPanelDialog(GraphNode node, bool connected)
    {
        Title = "Text Panel"; PrimaryButtonText = "Apply"; CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.None;
        AutomationProperties.SetAutomationId(this, "TextPanelDialog");
        _text = new TextBox { Text = node.Parameters.GetValueOrDefault("text", node.Component == "Text" ? "NurbsScene" : "") as string ?? "",
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 200, MaxLength = InputComponents.MaximumTextLength, FontSize = 12 };
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
