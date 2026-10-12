namespace DiskEraser;

/// <summary>
/// Settings window for diagram colours and other preferences.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly Action? _onSaved;

    private enum ColourTarget
    {
        Formattable,
        SystemDrive,
        NoLetter,
        Unallocated
    }

    private ColourTarget? _activeColourTarget;

    /// <param name="onSaved">Optional callback invoked when Save is clicked (e.g. to refresh the diagram).</param>
    public SettingsWindow(Action? onSaved = null)
    {
        _onSaved = onSaved;

        InitializeComponent();
        WindowChrome.TryApplyMica(this);
        var titleBar = (Content as FrameworkElement)?.FindName("SettingsTitleBar") as UIElement;
        WindowChrome.TryExtendTitleBar(this, titleBar);
        WindowChrome.TrySetSize(this, 400, 577);
        LoadThemeOptions();
        LoadCurrentTheme();
        LoadColourPresets();
        LoadCurrentColours();
        UpdateColourPreviews();
        App.ApplyTheme(this);
        WindowChrome.TryDisableMaximizeBox(this);
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
        ApplyLoadedColour(AppSettings.GetFormattableColour(), FormattableColourCombo, FormattableColourPreview);
        ApplyLoadedColour(AppSettings.GetSystemDriveColour(), SystemDriveColourCombo, SystemDriveColourPreview);
        ApplyLoadedColour(AppSettings.GetNoLetterColour(), NoLetterColourCombo, NoLetterColourPreview);
        ApplyLoadedColour(AppSettings.GetUnallocatedColour(), UnallocatedColourCombo, UnallocatedColourPreview);
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
        combo.SelectedIndex = -1;
    }

    private static void ApplyLoadedColour(string hex, ComboBox combo, Border preview)
    {
        SelectColour(combo, hex);
        var color = AppSettings.ParseColor(hex ?? AppSettings.DefaultFormattableColour);
        preview.Background = color != null ? new SolidColorBrush(color.Value) : null;
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

    private void ColourPreview_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not Border border)
        {
            return;
        }

        if (border == FormattableColourPreview)
        {
            _activeColourTarget = ColourTarget.Formattable;
            ColourPickerTitle.Text = "Formattable partitions";
        }
        else if (border == SystemDriveColourPreview)
        {
            _activeColourTarget = ColourTarget.SystemDrive;
            ColourPickerTitle.Text = "System drive (C:)";
        }
        else if (border == NoLetterColourPreview)
        {
            _activeColourTarget = ColourTarget.NoLetter;
            ColourPickerTitle.Text = "No drive letter";
        }
        else if (border == UnallocatedColourPreview)
        {
            _activeColourTarget = ColourTarget.Unallocated;
            ColourPickerTitle.Text = "Unallocated space";
        }
        else
        {
            _activeColourTarget = null;
            return;
        }

        if (border.Background is SolidColorBrush brush)
        {
            ColourPicker.Color = brush.Color;
        }

        ColourPickerFlyout.ShowAt(border);
    }

    private void ColourPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_activeColourTarget is null)
        {
            return;
        }

        var c = args.NewColor;
        var brush = new SolidColorBrush(c);

        switch (_activeColourTarget.Value)
        {
            case ColourTarget.Formattable:
                FormattableColourPreview.Background = brush;
                SyncComboToColor(FormattableColourCombo, c);
                break;
            case ColourTarget.SystemDrive:
                SystemDriveColourPreview.Background = brush;
                SyncComboToColor(SystemDriveColourCombo, c);
                break;
            case ColourTarget.NoLetter:
                NoLetterColourPreview.Background = brush;
                SyncComboToColor(NoLetterColourCombo, c);
                break;
            case ColourTarget.Unallocated:
                UnallocatedColourPreview.Background = brush;
                SyncComboToColor(UnallocatedColourCombo, c);
                break;
        }
    }

    private static void SyncComboToColor(ComboBox combo, Windows.UI.Color color)
    {
        for (int i = 0; i < AppSettings.ColourPresets.Length; i++)
        {
            var presetColor = AppSettings.ParseColor(AppSettings.ColourPresets[i].Hex);
            if (presetColor.HasValue && presetColor.Value.Equals(color))
            {
                combo.SelectedIndex = i;
                return;
            }
        }

        combo.SelectedIndex = -1;
    }

    private static string HexFromPreview(Border preview, string fallbackHex)
    {
        if (preview.Background is SolidColorBrush b)
        {
            var c = b.Color;
            return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        }
        return fallbackHex;
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
        else
        {
            AppSettings.SetFormattableColour(HexFromPreview(FormattableColourPreview, AppSettings.DefaultFormattableColour));
        }

        if (SystemDriveColourCombo.SelectedIndex >= 0 && SystemDriveColourCombo.SelectedIndex < AppSettings.ColourPresets.Length)
        {
            AppSettings.SetSystemDriveColour(AppSettings.ColourPresets[SystemDriveColourCombo.SelectedIndex].Hex);
        }
        else
        {
            AppSettings.SetSystemDriveColour(HexFromPreview(SystemDriveColourPreview, AppSettings.DefaultSystemDriveColour));
        }

        if (NoLetterColourCombo.SelectedIndex >= 0 && NoLetterColourCombo.SelectedIndex < AppSettings.ColourPresets.Length)
        {
            AppSettings.SetNoLetterColour(AppSettings.ColourPresets[NoLetterColourCombo.SelectedIndex].Hex);
        }
        else
        {
            AppSettings.SetNoLetterColour(HexFromPreview(NoLetterColourPreview, AppSettings.DefaultNoLetterColour));
        }

        if (UnallocatedColourCombo.SelectedIndex >= 0 && UnallocatedColourCombo.SelectedIndex < AppSettings.ColourPresets.Length)
        {
            AppSettings.SetUnallocatedColour(AppSettings.ColourPresets[UnallocatedColourCombo.SelectedIndex].Hex);
        }
        else
        {
            AppSettings.SetUnallocatedColour(HexFromPreview(UnallocatedColourPreview, AppSettings.DefaultUnallocatedColour));
        }

        _onSaved?.Invoke();
        App.NotifyDiagramColoursChanged();
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
