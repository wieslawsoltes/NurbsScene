using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Geometry;
using NurbsScene.Presentation;
using NurbsScene.Rendering;

namespace NurbsScene.Controls;

/// <summary>Native, compact property-grid camera editor. Drafts belong to one
/// viewport and are committed atomically by Apply.</summary>
public sealed class ViewportPropertiesPanel : StackPanel, IDisposable
{
    private readonly CadViewport _viewport;
    private readonly Func<bool> _isActive;
    private readonly TextBlock _heading = ClassicChrome.Label("Viewport properties", 12);
    private readonly TextBlock _projection = ClassicChrome.Label("", 11), _size = ClassicChrome.Label("", 11);
    private readonly TextBlock _error = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, TextBox> _fields = [];
    private readonly Dictionary<string, string> _fieldNames = [];
    private readonly Dictionary<TextBox, string> _presentedText = [];
    private readonly ComboBox _display = new() { FontSize = 11, MinWidth = 0, MinHeight = 26, Padding = new(5, 2, 20, 2), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private string _camera = "";
    private bool _draft, _refreshing, _disposed, _scrollAttached;
    private ViewportCameraSettings? _snapshot;
    public bool IsPanelActive => _isActive();
    public bool HasDraft => _draft;
    public string Validation => _error.Text;

    public ViewportPropertiesPanel(CadViewport viewport, Func<bool>? isActive = null)
    {
        ArgumentNullException.ThrowIfNull(viewport); _viewport = viewport; _isActive = isActive ?? (() => true); Spacing = 2;
        AutomationProperties.SetAutomationId(this, "ViewportProperties");
        _heading.Margin = new(0, 3, 0, 5); Children.Add(_heading);
        Children.Add(ClassicChrome.SectionHeader("Viewport"));
        Children.Add(InspectorLayout.Row("Projection", _projection));
        Children.Add(InspectorLayout.Row("Size", _size));
        _display.ItemsSource = Enum.GetNames<DisplayMode>();
        AutomationProperties.SetAutomationId(_display, "ViewportDisplayMode");
        AutomationProperties.SetName(_display, "Display mode for all viewports");
        ToolTipService.SetToolTip(_display, "Display mode applies to all model viewports.");
        Children.Add(InspectorLayout.Row("Display mode", _display));
        _display.SelectionChanged += (_, _) =>
        {
            if (_refreshing || _display.SelectedItem is not string name || !Enum.TryParse<DisplayMode>(name, out var mode)) return;
            _viewport.Scene.Mode = mode; _viewport.NotifyViewChanged();
        };
        void Field(string key, string label)
        {
            var box = InspectorLayout.Editor("", label, "ViewportProperty" + key);
            box.TextChanged += (_, _) =>
            {
                if (_refreshing || (_presentedText.TryGetValue(box, out var presented) && box.Text == presented)) return;
                _draft = true; SetValidation("Unapplied camera changes", false);
            };
            _fields.Add(key, box); _fieldNames.Add(key, label); Children.Add(InspectorLayout.Row(label, box));
        }
        Children.Add(ClassicChrome.SectionHeader("Camera target"));
        Field("X", "Target X"); Field("Y", "Target Y"); Field("Z", "Target Z");
        Children.Add(ClassicChrome.SectionHeader("Camera"));
        Field("Distance", "Distance"); Field("Scale", "Parallel height");
        Field("Azimuth", "Azimuth (°)"); Field("Elevation", "Elevation (°)"); Field("Fov", "Field of view (°)");
        var actions = new Grid { ColumnSpacing = 5, Margin = new(0, 7, 0, 3) };
        actions.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var apply = ClassicChrome.Button("Apply", Apply, "ApplyViewportProperties"); apply.HorizontalAlignment = HorizontalAlignment.Stretch;
        var revert = ClassicChrome.Button("Revert", () => { _draft = false; _snapshot = null; Refresh(); }, "RevertViewportProperties"); revert.HorizontalAlignment = HorizontalAlignment.Stretch;
        actions.Children.Add(apply); actions.Children.Add(revert); Grid.SetColumn(revert, 1); Children.Add(actions); Children.Add(_error);
        AutomationProperties.SetAutomationId(_error, "ViewportPropertiesValidation");
        Children.Add(new TextBlock
        {
            Text = "Apply changes this camera. Switching views discards unapplied camera edits.",
            FontSize = 10, Foreground = ClassicChrome.MutedInk, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0),
        });
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); Refresh(); };
        _viewport.ViewChanged += QueueRefresh; _viewport.SizeChanged += OnSizeChanged; Refresh();
    }
    internal void AttachScrolling(ScrollViewer inspector)
    {
        if (_scrollAttached) return; _scrollAttached = true;
        foreach (var field in _fields.Values) InspectorScrolling.Attach(field, inspector);
    }
    private void SetValidation(string text, bool error)
    {
        _error.Text = text; _error.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _error.Foreground = error ? ClassicChrome.Brush(150, 40, 35) : ClassicChrome.MutedInk;
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => QueueRefresh();
    private void QueueRefresh()
    {
        if (_disposed || !IsPanelActive) return;
        if (_camera != _viewport.ActiveViewport) { Refresh(); return; }
        if (!_refreshTimer.IsEnabled) _refreshTimer.Start();
    }
    public void Refresh()
    {
        if (_disposed || !IsPanelActive) return;
        var camera = _viewport.Cameras[_viewport.ActiveViewport];
        _heading.Text = camera.Name + " viewport"; _projection.Text = camera.Perspective ? "Perspective" : "Parallel";
        var rectangle = ViewportLayout.Create(Math.Max(1, (int)_viewport.ActualWidth), Math.Max(1, (int)_viewport.ActualHeight), _viewport.Cameras, _viewport.Maximized).FirstOrDefault(v => v.Name == camera.Name);
        _size.Text = rectangle is null ? "Not currently visible" : $"{rectangle.Width} × {rectangle.Height} DIP";
        if (_camera != camera.Name) { _camera = camera.Name; _draft = false; _snapshot = null; }
        _refreshing = true;
        try
        {
            _display.SelectedItem = _viewport.Scene.Mode.ToString();
            if (_draft) return;
            var settings = ViewportCameraSettings.Read(camera); if (settings == _snapshot) return;
            _snapshot = settings; SetValidation("", false);
            var values = new Dictionary<string, double>
            {
                {"X", settings.Target.X}, {"Y", settings.Target.Y}, {"Z", settings.Target.Z}, {"Distance", settings.Distance}, {"Scale", settings.Scale},
                {"Azimuth", settings.Azimuth * 180 / Math.PI}, {"Elevation", settings.Elevation * 180 / Math.PI}, {"Fov", settings.FieldOfView * 180 / Math.PI},
            };
            foreach (var (name, box) in _fields)
            {
                var text = values[name].ToString("G17", CultureInfo.InvariantCulture); _presentedText[box] = text; box.Text = text;
            }
            _fields["Scale"].IsEnabled = !camera.Perspective;
            foreach (var name in new[] { "Azimuth", "Elevation", "Fov" }) _fields[name].IsEnabled = camera.Perspective;
        }
        finally { _refreshing = false; }
    }
    private void Apply()
    {
        if (_camera != _viewport.ActiveViewport) { _draft = false; _snapshot = null; Refresh(); return; }
        try
        {
            double Number(string name) => double.TryParse(_fields[name].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
                ? value : throw new ArgumentException($"{_fieldNames[name]}: enter a finite number using a decimal point.");
            var camera = _viewport.Cameras[_camera];
            var draft = new ViewportCameraSettings(new Vec3(Number("X"), Number("Y"), Number("Z")), Number("Distance"), Number("Scale"),
                Number("Azimuth") * Math.PI / 180, Number("Elevation") * Math.PI / 180, Number("Fov") * Math.PI / 180);
            _viewport.CancelNavigation(); draft.Apply(camera); _draft = false; _snapshot = null; _viewport.NotifyViewChanged(); Refresh();
        }
        catch (Exception error) { SetValidation(error.Message, true); }
    }
    public new void Dispose()
    {
        if (_disposed) return; _disposed = true; _refreshTimer.Stop(); _viewport.ViewChanged -= QueueRefresh; _viewport.SizeChanged -= OnSizeChanged; base.Dispose();
    }
}
