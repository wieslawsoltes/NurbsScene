using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NurbsScene.Model;

namespace NurbsScene.Controls;

/// <summary>Shared, narrow-container property rows. Labels and editors have
/// independent columns so long numeric values keep their editing width.</summary>
internal static class InspectorLayout
{
    public static Grid Row(string caption, FrameworkElement value, double labelWidth = 89)
    {
        var row = new Grid { MinHeight = 29, ColumnSpacing = 7, Margin = new(0, 1, 0, 1) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(labelWidth) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var label = ClassicChrome.Label(caption, 11);
        label.Foreground = ClassicChrome.MutedInk;
        label.TextWrapping = TextWrapping.Wrap;
        label.Margin = new(0, 3, 0, 3);
        row.Children.Add(label);
        value.HorizontalAlignment = HorizontalAlignment.Stretch;
        value.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(value); Grid.SetColumn(value, 1);
        return row;
    }

    public static Grid Value(string caption, string text)
    {
        var value = ClassicChrome.Label(text, 11);
        value.TextWrapping = TextWrapping.Wrap;
        return Row(caption, value);
    }

    public static TextBox Editor(string text, string name, string? id = null)
    {
        var field = new TextBox
        {
            Text = text, FontSize = 11, MinHeight = 26, MinWidth = 0,
            Padding = new(5, 2, 5, 2), CornerRadius = new(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(field, name);
        if (id is not null) AutomationProperties.SetAutomationId(field, id);
        return field;
    }

    public static byte Channel(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
    public static string Hex(ColorRgba color) => $"#{Channel(color.R):X2}{Channel(color.G):X2}{Channel(color.B):X2}";
    public static SolidColorBrush ColorBrush(ColorRgba color) =>
        new(Microsoft.UI.ColorHelper.FromArgb(Channel(color.A), Channel(color.R), Channel(color.G), Channel(color.B)));

    public static Border Swatch(ColorRgba color, double size = 15) => new()
    {
        Width = size, Height = size, Background = ColorBrush(color),
        BorderBrush = ClassicChrome.Border, BorderThickness = new(1), VerticalAlignment = VerticalAlignment.Center,
    };
}
