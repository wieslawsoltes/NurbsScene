using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Geometry;
using NurbsScene.Rendering;

namespace NurbsScene.Controls;

/// <summary>Native draft editor; only a validated Apply changes the live renderer.</summary>
public sealed class RenderingSettingsDialog : ContentDialog
{
    private readonly WebGpuRenderSettings _original;
    private readonly Dictionary<string, TextBox> _fields = [];
    private readonly CheckBox _shadows = new() { Content = "Soft shadows", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _error = new()
    {
        FontSize = 11, TextWrapping = TextWrapping.Wrap,
        Foreground = ClassicChrome.Brush(170, 45, 30)
    };
    public WebGpuRenderSettings? Edit { get; private set; }
    public string ValidationMessage => _error.Text;

    public RenderingSettingsDialog(WebGpuRenderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        _original = settings;
        Title = "Rendering";
        PrimaryButtonText = "Apply";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        FontSize = 12;
        AutomationProperties.SetAutomationId(this, "RenderingSettingsDialog");
        var content = new StackPanel { Width = 380, Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "Studio lighting and material appearance for all viewports.",
            FontSize = 12, TextWrapping = TextWrapping.Wrap
        });
        TextBox Field(string key, string label)
        {
            var box = new TextBox
            {
                Header = label, FontSize = 12, MinHeight = 28, MaxLength = 40,
                Padding = new(6, 3, 6, 3), HorizontalAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetAutomationId(box, "Rendering" + key);
            AutomationProperties.SetName(box, label);
            box.TextChanged += (_, _) => { Edit = null; _error.Text = ""; };
            _fields.Add(key, box);
            return box;
        }
        void Row(params FrameworkElement[] fields)
        {
            var grid = new Grid { ColumnSpacing = 12 };
            for (var index = 0; index < fields.Length; index++)
            {
                grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                grid.Children.Add(fields[index]); Grid.SetColumn(fields[index], index);
            }
            content.Children.Add(grid);
        }
        Row(Field("Exposure", "Exposure (>0–32)"), Field("Bloom", "Bloom (0–1)"));
        Row(Field("Metallic", "Metallic (0–1)"), Field("Roughness", "Roughness (0.04–1)"));
        Row(Field("Ambient", "Ambient light (0–32)"), Field("Light", "Key light (0–128)"));
        Row(Field("DirectionX", "Light direction X"), Field("DirectionY", "Light direction Y"), Field("DirectionZ", "Light direction Z"));
        AutomationProperties.SetAutomationId(_shadows, "RenderingShadows");
        _shadows.Checked += (_, _) => { Edit = null; _error.Text = ""; };
        _shadows.Unchecked += (_, _) => { Edit = null; _error.Text = ""; };
        content.Children.Add(_shadows);
        var drafts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        drafts.Children.Add(ClassicChrome.Button("Revert draft", () => Populate(_original), "RenderingRevert"));
        drafts.Children.Add(ClassicChrome.Button("Defaults", () => Populate(new()), "RenderingDefaults"));
        content.Children.Add(drafts);
        content.Children.Add(new TextBlock
        {
            Text = "Light direction is normalized. Apply saves these display preferences on this device; geometry, camera positions and document undo remain unchanged.",
            FontSize = 10, TextWrapping = TextWrapping.Wrap
        });
        AutomationProperties.SetAutomationId(_error, "RenderingValidation");
        content.Children.Add(_error);
        Content = new ScrollViewer
        {
            Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Populate(settings);
        PrimaryButtonClick += (_, args) =>
        {
            try
            {
                double Number(string name)
                {
                    if (!double.TryParse(_fields[name].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                        throw new ArgumentException(_fields[name].Header + ": enter a finite number using a decimal point.");
                    return value;
                }
                var candidate = new WebGpuRenderSettings
                {
                    Exposure = (float)Number("Exposure"), BloomStrength = (float)Number("Bloom"),
                    Metallic = (float)Number("Metallic"), Roughness = (float)Number("Roughness"),
                    AmbientIntensity = (float)Number("Ambient"), LightIntensity = (float)Number("Light"),
                    LightDirection = new Vec3(Number("DirectionX"), Number("DirectionY"), Number("DirectionZ")),
                    Shadows = _shadows.IsChecked == true
                };
                candidate.Validate(); Edit = candidate; _error.Text = "";
            }
            catch (ArgumentException error)
            {
                args.Cancel = true; Edit = null; _error.Text = error.Message;
            }
        };
    }

    private void Populate(WebGpuRenderSettings settings)
    {
        void Value(string name, float value) => _fields[name].Text = value.ToString("R", CultureInfo.InvariantCulture);
        Value("Exposure", settings.Exposure); Value("Bloom", settings.BloomStrength);
        Value("Metallic", settings.Metallic); Value("Roughness", settings.Roughness);
        Value("Ambient", settings.AmbientIntensity); Value("Light", settings.LightIntensity);
        _fields["DirectionX"].Text = settings.LightDirection.X.ToString("R", CultureInfo.InvariantCulture);
        _fields["DirectionY"].Text = settings.LightDirection.Y.ToString("R", CultureInfo.InvariantCulture);
        _fields["DirectionZ"].Text = settings.LightDirection.Z.ToString("R", CultureInfo.InvariantCulture);
        _shadows.IsChecked = settings.Shadows; Edit = null; _error.Text = "";
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PrimaryButton") is FrameworkElement apply) AutomationProperties.SetAutomationId(apply, "RenderingApply");
        if (GetTemplateChild("CloseButton") is FrameworkElement cancel) AutomationProperties.SetAutomationId(cancel, "RenderingCancel");
    }
}
