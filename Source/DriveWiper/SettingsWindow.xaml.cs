using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Microsoft.UI.Composition.SystemBackdrops;
using WinRT.Interop;
using DriveWiper.Services;

namespace DriveWiper;

/// <summary>
/// Settings window for diagram colours and other preferences.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly Action? _onSaved;

    /// <param name="onSaved">Optional callback invoked when Save is clicked (e.g. to refresh the diagram).</param>
    public SettingsWindow(Action? onSaved = null)
    {
        _onSaved = onSaved;
        InitializeComponent();
        TrySetMicaBackdrop();
        TrySetExtendedTitleBar();
        TrySetWindowSize(400, 380);
        LoadColourPresets();
        LoadCurrentColours();
        UpdateColourPreviews();
    }

    private void LoadColourPresets()
    {
        foreach (var (name, _) in AppSettings.ColourPresets)
        {
            FormattableColourCombo.Items.Add(name);
            SystemDriveColourCombo.Items.Add(name);
            NoLetterColourCombo.Items.Add(name);
        }
    }

    private void LoadCurrentColours()
    {
        SelectColour(FormattableColourCombo, AppSettings.GetFormattableColour());
        SelectColour(SystemDriveColourCombo, AppSettings.GetSystemDriveColour());
        SelectColour(NoLetterColourCombo, AppSettings.GetNoLetterColour());
    }

    /// <summary>Applies Mica backdrop on Windows 11; no-op on older Windows.</summary>
    private void TrySetMicaBackdrop()
    {
        try
        {
            if (MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop
                {
                    Kind = MicaKind.BaseAlt
                };
            }
        }
        catch
        {
            // Mica not supported (e.g. Windows 10)
        }
    }

    /// <summary>Extends content into the title bar so Mica shows there; sets the drag region.</summary>
    private void TrySetExtendedTitleBar()
    {
        try
        {
            var titleBar = (Content as FrameworkElement)?.FindName("SettingsTitleBar") as Grid;
            if (titleBar != null)
            {
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(titleBar);
            }
        }
        catch
        {
            // Fall back to default title bar
        }
    }

    private static void SelectColour(ComboBox combo, string hex)
    {
        for (int i = 0; i < AppSettings.ColourPresets.Length; i++)
        {
            if (string.Equals(AppSettings.ColourPresets[i].Hex, hex, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private void ColourCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateColourPreviews();
    }

    /// <summary>Updates the colour preview swatches from the current combo selections.</summary>
    private void UpdateColourPreviews()
    {
        var root = Content as FrameworkElement;
        SetPreviewFromCombo(root?.FindName("FormattableColourPreview") as Border, FormattableColourCombo);
        SetPreviewFromCombo(root?.FindName("SystemDriveColourPreview") as Border, SystemDriveColourCombo);
        SetPreviewFromCombo(root?.FindName("NoLetterColourPreview") as Border, NoLetterColourCombo);
    }

    private static void SetPreviewFromCombo(Border preview, ComboBox combo)
    {
        if (preview == null || combo == null) return;
        string? hex = null;
        if (combo.SelectedIndex >= 0 && combo.SelectedIndex < AppSettings.ColourPresets.Length)
            hex = AppSettings.ColourPresets[combo.SelectedIndex].Hex;
        var color = AppSettings.ParseColor(hex ?? AppSettings.DefaultFormattableColour);
        preview.Background = color != null ? new SolidColorBrush(color.Value) : null;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (FormattableColourCombo.SelectedIndex >= 0 && FormattableColourCombo.SelectedIndex < AppSettings.ColourPresets.Length)
            AppSettings.SetFormattableColour(AppSettings.ColourPresets[FormattableColourCombo.SelectedIndex].Hex);
        if (SystemDriveColourCombo.SelectedIndex >= 0 && SystemDriveColourCombo.SelectedIndex < AppSettings.ColourPresets.Length)
            AppSettings.SetSystemDriveColour(AppSettings.ColourPresets[SystemDriveColourCombo.SelectedIndex].Hex);
        if (NoLetterColourCombo.SelectedIndex >= 0 && NoLetterColourCombo.SelectedIndex < AppSettings.ColourPresets.Length)
            AppSettings.SetNoLetterColour(AppSettings.ColourPresets[NoLetterColourCombo.SelectedIndex].Hex);
        _onSaved?.Invoke();
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TrySetWindowSize(int width, int height)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        }
        catch { /* ignore */ }
    }
}
