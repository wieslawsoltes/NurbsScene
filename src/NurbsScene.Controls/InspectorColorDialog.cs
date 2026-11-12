using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Model;

namespace NurbsScene.Controls;

/// <summary>A color draft commits once through its owner's document transaction.
/// Palette selection, typing, validation and Cancel never change the document.</summary>
public sealed class InspectorColorDialog : ContentDialog
{
    private readonly ColorRgba _source;
    private readonly string _initialHex;
    private readonly double _initialOpacity;
    private readonly TextBox _hex;
    private readonly TextBox _opacity;
    private readonly Border _preview;
    private readonly TextBlock _error = new()
    {
        FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = ClassicChrome.Brush(164, 47, 37),
    };
    public ColorRgba? SelectedColor { get; private set; }
    public string ValidationMessage => _error.Text;

    public InspectorColorDialog(string title, ColorRgba source)
    {
        if (!source.IsValid) throw new ArgumentException("A valid source color is required.", nameof(source));
        Title = title; PrimaryButtonText = "Apply"; CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        AutomationProperties.SetAutomationId(this, "InspectorColorDialog");
        _source = source; _initialHex = InspectorLayout.Hex(source);
        var opacityText = (source.A * 100d).ToString("G9", CultureInfo.InvariantCulture);
        _initialOpacity = double.Parse(opacityText, CultureInfo.InvariantCulture);
        _hex = InspectorLayout.Editor(_initialHex, "Hex color", "InspectorColorHex"); _hex.MaxLength = 7;
        _opacity = InspectorLayout.Editor(opacityText, "Opacity percent", "InspectorColorOpacity");
        var content = new StackPanel { Width = 310, Spacing = 10 };
        content.Children.Add(ClassicChrome.Label("Display color", 12));
        var palette = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
        for (var i = 0; i < 8; i++) palette.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        palette.RowDefinitions.Add(new() { Height = new GridLength(29) });
        palette.RowDefinitions.Add(new() { Height = new GridLength(29) });
        var colors = new[] { "#242424", "#606060", "#A0A0A0", "#E1E1E1", "#FFFFFF", "#C15D4E", "#E19247", "#E3CA64",
            "#7DA56A", "#4C9C8A", "#5A9FB7", "#537FA8", "#746AA7", "#AB759E", "#B8C8CA", "#8B705D" };
        for (var i = 0; i < colors.Length; i++)
        {
            var hex = colors[i]; var color = Parse(hex, 100);
            var button = ClassicChrome.Button(hex, () => _hex.Text = hex, "InspectorColorPreset" + i);
            button.Content = InspectorLayout.Swatch(color, 22); button.Height = 29; button.Padding = new(2);
            ToolTipService.SetToolTip(button, hex); palette.Children.Add(button); Grid.SetColumn(button, i % 8); Grid.SetRow(button, i / 8);
        }
        content.Children.Add(palette);
        content.Children.Add(InspectorLayout.Row("Hex color", _hex));
        content.Children.Add(InspectorLayout.Row("Opacity (%)", _opacity));
        _preview = InspectorLayout.Swatch(source, 48); _preview.Width = double.NaN;
        content.Children.Add(_preview);
        content.Children.Add(new TextBlock { Text = "Enter #RRGGBB. Apply stores one undoable display-color change.", FontSize = 10, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(_error); Content = content;
        AutomationProperties.SetAutomationId(_error, "InspectorColorValidation");
        _hex.TextChanged += (_, _) => DraftChanged(); _opacity.TextChanged += (_, _) => DraftChanged();
        PrimaryButtonClick += (_, args) =>
        {
            try { SelectedColor = ReadDraft(); _error.Text = ""; }
            catch (ArgumentException error) { SelectedColor = null; args.Cancel = true; _error.Text = error.Message; }
        };
    }

    private ColorRgba ReadDraft()
    {
        if (!double.TryParse(_opacity.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity)
            || !double.IsFinite(opacity) || opacity < 0 || opacity > 100)
            throw new ArgumentException("Opacity must be a number from 0 to 100.");
        var color = Parse(_hex.Text, opacity);
        // Hex is an eight-bit presentation of authoritative floating channels.
        // Opening and applying an unchanged draft must not quantize those values;
        // changing only opacity likewise preserves the original RGB channels.
        if (string.Equals(_hex.Text.Trim().TrimStart('#'), _initialHex[1..], StringComparison.OrdinalIgnoreCase))
            color = color with { R = _source.R, G = _source.G, B = _source.B };
        if (opacity == _initialOpacity) color = color with { A = _source.A };
        return color;
    }
    private static ColorRgba Parse(string text, double opacity)
    {
        var hex = text.Trim(); if (hex.StartsWith('#')) hex = hex[1..];
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
            throw new ArgumentException("Enter six hexadecimal color digits, for example #5A9FB7.");
        return new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, (float)(opacity / 100));
    }
    private void DraftChanged()
    {
        SelectedColor = null; _error.Text = "";
        try { _preview.Background = InspectorLayout.ColorBrush(ReadDraft()); }
        catch (ArgumentException) { /* A partial draft leaves the last valid preview visible. */ }
    }
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PrimaryButton") is FrameworkElement apply) AutomationProperties.SetAutomationId(apply, "InspectorColorApply");
        if (GetTemplateChild("CloseButton") is FrameworkElement cancel) AutomationProperties.SetAutomationId(cancel, "InspectorColorCancel");
    }
}
