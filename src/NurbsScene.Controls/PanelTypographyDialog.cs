using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NurbsScene.Parametric;

namespace NurbsScene.Controls;

/// <summary>Reusable draft-only native editor. No graph or document is retained;
/// valid Apply returns one style, while Cancel, Reset and preview stay local.</summary>
public sealed class PanelTypographyDialog : ContentDialog
{
    private readonly TextBox _family, _size;
    private readonly CheckBox _bold, _italic;
    private readonly ComboBox _horizontal, _vertical;
    private readonly TextBlock _error = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = ClassicChrome.Brush(170,45,30) };
    public PanelTextStyle? Edit { get; private set; }
    public string ValidationMessage => _error.Text;
    public PanelTypographyDialog(GraphNode node)
    {
        if (node.Component is not ("Panel" or "Text")) throw new ArgumentException("Select a text panel.");
        var original = PanelTextStyle.For(node); original.Validate();
        Title = "Panel Typography"; PrimaryButtonText = "Apply"; CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary; FontSize = 12;
        AutomationProperties.SetAutomationId(this, "PanelTypographyDialog");
        TextBox Field(string header, string text, string id)
        {
            var field = new TextBox { Header = header, Text = text, FontSize = 12, MinHeight = 28, Padding = new(6,3,6,3) };
            AutomationProperties.SetAutomationId(field, id); AutomationProperties.SetName(field, header); return field;
        }
        _family = Field("Font family (empty = host default)", original.FontFamily, "TypographyFamily"); _family.MaxLength = 128;
        _size = Field("Size at 100% zoom (6–72)", original.Size.ToString("R", CultureInfo.InvariantCulture), "TypographySize"); _size.MaxLength = 32;
        _bold = new() { Content = "Bold", IsChecked = original.Bold, FontSize = 12 };
        _italic = new() { Content = "Italic", IsChecked = original.Italic, FontSize = 12 };
        AutomationProperties.SetAutomationId(_bold, "TypographyBold"); AutomationProperties.SetAutomationId(_italic, "TypographyItalic");
        _horizontal = new() { Header = "Horizontal alignment", ItemsSource = new[] { "Left", "Center", "Right" }, SelectedIndex = (int)original.Horizontal, HorizontalAlignment = HorizontalAlignment.Stretch };
        _vertical = new() { Header = "Vertical alignment", ItemsSource = new[] { "Top", "Center", "Bottom" }, SelectedIndex = (int)original.Vertical, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(_horizontal, "TypographyHorizontal"); AutomationProperties.SetAutomationId(_vertical, "TypographyVertical");
        var content = new StackPanel { Width = 360, Spacing = 8 };
        content.Children.Add(_family); content.Children.Add(_size);
        var styles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 }; styles.Children.Add(_bold); styles.Children.Add(_italic); content.Children.Add(styles);
        var alignments = new Grid { ColumnSpacing = 12 };
        alignments.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); alignments.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        alignments.Children.Add(_horizontal); alignments.Children.Add(_vertical); Grid.SetColumn(_vertical, 1); content.Children.Add(alignments);
        var preview = new TextBlock { Text = "NurbsScene\nWWW   iii", FontSize = original.Size };
        content.Children.Add(new Border { Height = 100, Padding = new(8), Background = ClassicChrome.Brush(249,245,163), Child = preview });
        content.Children.Add(ClassicChrome.Button("Reset typography", () => {
            _family.Text = ""; _size.Text = "10"; _bold.IsChecked = false; _italic.IsChecked = false; _horizontal.SelectedIndex = 0; _vertical.SelectedIndex = 0;
        }, "TypographyReset"));
        content.Children.Add(new TextBlock { Text = "Unavailable font families use host fallback. Alignment affects panel body text; paths, indices and data remain unchanged.", TextWrapping = TextWrapping.Wrap, FontSize = 10 });
        AutomationProperties.SetAutomationId(_error, "TypographyValidation"); content.Children.Add(_error); Content = content;
        void Changed()
        {
            Edit = null; _error.Text = "";
            if (double.TryParse(_size.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && double.IsFinite(size) && size >= 6 && size <= 72) preview.FontSize = size;
            preview.FontWeight = _bold.IsChecked == true ? Windows.UI.Text.FontWeights.Bold : Windows.UI.Text.FontWeights.Normal;
            preview.FontStyle = _italic.IsChecked == true ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
            preview.TextAlignment = _horizontal.SelectedIndex switch { 1 => TextAlignment.Center, 2 => TextAlignment.Right, _ => TextAlignment.Left };
            preview.VerticalAlignment = _vertical.SelectedIndex switch { 1 => VerticalAlignment.Center, 2 => VerticalAlignment.Bottom, _ => VerticalAlignment.Top };
            // Native preview uses the native host text control; the main canvas
            // resolves its Skia font independently and reports that resolved family.
            var family = _family.Text.Trim();
            try { (PanelTextStyle.Default with { FontFamily = family }).Validate(); }
            catch (ArgumentException) { family = ""; }
            preview.FontFamily = family.Length == 0 ? FontFamily.XamlAutoFontFamily : new FontFamily(family);
        }
        foreach (var field in new[] { _family, _size }) field.TextChanged += (_,_) => Changed();
        _bold.Checked += (_,_) => Changed(); _bold.Unchecked += (_,_) => Changed();
        _italic.Checked += (_,_) => Changed(); _italic.Unchecked += (_,_) => Changed();
        _horizontal.SelectionChanged += (_,_) => Changed(); _vertical.SelectionChanged += (_,_) => Changed(); Changed();
        PrimaryButtonClick += (_, args) =>
        {
            try
            {
                if (!double.TryParse(_size.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var size)) throw new ArgumentException("Enter a font size using a dot as decimal separator.");
                var style = new PanelTextStyle(_family.Text.Trim(), size, _bold.IsChecked == true, _italic.IsChecked == true,
                    (PanelHorizontalAlignment)_horizontal.SelectedIndex, (PanelVerticalAlignment)_vertical.SelectedIndex);
                style.Validate(); Edit = style; _error.Text = "";
            }
            catch (ArgumentException error) { args.Cancel = true; Edit = null; _error.Text = error.Message; }
        };
    }
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PrimaryButton") is FrameworkElement apply) AutomationProperties.SetAutomationId(apply, "TypographyApply");
        if (GetTemplateChild("CloseButton") is FrameworkElement cancel) AutomationProperties.SetAutomationId(cancel, "TypographyCancel");
    }
}
