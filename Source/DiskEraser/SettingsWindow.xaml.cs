namespace DiskEraser;

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
        TrySetWindowSize(400, 577);
        LoadThemeOptions();
        LoadCurrentTheme();
        LoadColourPresets();
        LoadCurrentColours();
        UpdateColourPreviews();
        App.ApplyTheme(this);
        WindowUtilities.TryDisableMaximizeBox(this);
    }

    private void LoadThemeOptions()
    {
        ThemeCombo.Items.Clear();
        ThemeCombo.Items.Add("Light");
        ThemeCombo.Items.Add("Dark");
        ThemeCombo.Items.Add("Use system default");
    }

    private void LoadCurrentTheme()
    {
        string theme = AppSettings.GetTheme();
        ThemeCombo.SelectedIndex = theme switch
        {
            AppSettings.ThemeLight => 0,
            AppSettings.ThemeDark => 1,
            _ => 2
        };
    }

    private void LoadColourPresets()
    {
        foreach (var (name, _) in AppSettings.ColourPresets)
        {
            FormattableColourCombo.Items.Add(name);
            SystemDriveColourCombo.Items.Add(name);
            NoLetterColourCombo.Items.Add(name);
            UnallocatedColourCombo.Items.Add(name);
        }
    }

    private void LoadCurrentColours()
    {
        SelectColour(FormattableColourCombo, AppSettings.GetFormattableColour());
        SelectColour(SystemDriveColourCombo, AppSettings.GetSystemDriveColour());
        SelectColour(NoLetterColourCombo, AppSettings.GetNoLetterColour());
        SelectColour(UnallocatedColourCombo, AppSettings.GetUnallocatedColour());
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
        SetPreviewFromCombo(root?.FindName("UnallocatedColourPreview") as Border, UnallocatedColourCombo);
    }

    private static void SetPreviewFromCombo(Border? preview, ComboBox? combo)
    {
        if (preview == null || combo == null)
        {
            return;
        }

        string? hex = null;
        if (combo.SelectedIndex >= 0 && combo.SelectedIndex < AppSettings.ColourPresets.Length)
        {
            hex = AppSettings.ColourPresets[combo.SelectedIndex].Hex;
        }

        var color = AppSettings.ParseColor(hex ?? AppSettings.DefaultFormattableColour);
        preview.Background = color != null ? new SolidColorBrush(color.Value) : null;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (ThemeCombo.SelectedIndex >= 0)
        {
            string theme = ThemeCombo.SelectedIndex switch
            {
                0 => AppSettings.ThemeLight,
                1 => AppSettings.ThemeDark,
                _ => AppSettings.ThemeDefault
            };
            AppSettings.SetTheme(theme);
            App.ApplyTheme(this);
            if (App.MainWindow != null)
            {
                App.ApplyTheme(App.MainWindow);
            }
        }
        if (FormattableColourCombo.SelectedIndex >= 0 && FormattableColourCombo.SelectedIndex < AppSettings.ColourPresets.Length)
        {
            AppSettings.SetFormattableColour(AppSettings.ColourPresets[FormattableColourCombo.SelectedIndex].Hex);
        }

        if (SystemDriveColourCombo.SelectedIndex >= 0 && SystemDriveColourCombo.SelectedIndex < AppSettings.ColourPresets.Length)
        {
            AppSettings.SetSystemDriveColour(AppSettings.ColourPresets[SystemDriveColourCombo.SelectedIndex].Hex);
        }

        if (NoLetterColourCombo.SelectedIndex >= 0 && NoLetterColourCombo.SelectedIndex < AppSettings.ColourPresets.Length)
        {
            AppSettings.SetNoLetterColour(AppSettings.ColourPresets[NoLetterColourCombo.SelectedIndex].Hex);
        }

        if (UnallocatedColourCombo.SelectedIndex >= 0 && UnallocatedColourCombo.SelectedIndex < AppSettings.ColourPresets.Length)
        {
            AppSettings.SetUnallocatedColour(AppSettings.ColourPresets[UnallocatedColourCombo.SelectedIndex].Hex);
        }

        _onSaved?.Invoke();
        App.NotifyDiagramColoursChanged();
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
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        }
        catch { /* ignore */ }
    }

    // Win32 interop for modifying window styles
}
