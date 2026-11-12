using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Presentation;
using Windows.Storage;

namespace NurbsScene.Controls;

public sealed partial class WorkspaceView
{
    private const string RenderingPreferenceKey = "NurbsScene.Rendering.Settings.v1";
    private RenderingSettingsDialog? _renderingSettingsDialog;
    public RenderingSettingsDialog? RenderingSettingsEditor => _renderingSettingsDialog;
    public bool RenderingSettingsEditorIsOpen => _renderingSettingsDialog?.IsLoaded == true;
    public string RenderingSettingsValidation => _renderingSettingsDialog?.ValidationMessage ?? "";

    private void LoadRenderingPreferences()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(RenderingPreferenceKey, out var stored)
                && RenderSettingsPreferences.TryParse(stored as string, out var settings))
                Viewport.Canvas.RenderSettings = settings;
        }
        catch (Exception error) { Console.WriteLine("Rendering preferences: " + error.Message); }
    }

    private async Task RenderingSettingsAsync()
    {
        if (_disposed || _renderingSettingsDialog is not null || XamlRoot is null) return;
        var dialog = new RenderingSettingsDialog(Viewport.Canvas.RenderSettings) { XamlRoot = XamlRoot };
        _renderingSettingsDialog = dialog;
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || _disposed || dialog.Edit is not { } settings) return;
            // Validate and serialize before changing either the renderer or stored value.
            var encoded = RenderSettingsPreferences.Serialize(settings);
            Viewport.Canvas.RenderSettings = settings;
            try { ApplicationData.Current.LocalSettings.Values[RenderingPreferenceKey] = encoded; }
            catch (Exception error) { Console.WriteLine("Rendering settings applied, but could not be saved: " + error.Message); }
        }
        finally
        {
            _renderingSettingsDialog = null;
            if (!_disposed && Viewport.Canvas.IsLoaded) Viewport.Canvas.Focus(FocusState.Programmatic);
        }
    }
}
