namespace DiskEraser;

/// <summary>
/// Main window for the Disk Eraser app: lists formattable drives, format options, and runs format via <see cref="DriveService"/>.
/// Protected system/boot/EFI/recovery volumes cannot be formatted.
/// </summary>
public sealed partial class MainWindow : Window
{
    private IReadOnlyList<DriveItem> _drives = [];
    private IReadOnlyList<DiskInfo> _lastDisks = [];
    private string? _cachedFormattableHex, _cachedSystemHex, _cachedNoLetterHex;
    private SolidColorBrush? _cachedFormattableBrush, _cachedSystemBrush, _cachedNoLetterBrush, _cachedWhiteBrush;

    /// <summary>
    /// Gets or sets a value indicating whether [get is secure delete active].
    /// </summary>
    /// <value>
    ///   <c>true</c> if [get is secure delete active]; otherwise, <c>false</c>.
    /// </value>
    public bool GetIsSecureDeleteActive { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        ClusterSizeBox.InputScope = new InputScope();
        ClusterSizeBox.InputScope.Names.Add(new InputScopeName(InputScopeNameValue.Number));
        TrySetMicaBackdrop();
        TrySetExtendedTitleBar();
        TrySetWindowSize(1500, 800);
        LoadFileSystems();
        LoadClusterSizePresets();
        LoadSecureWipePresets();
        SetDriveLayoutContextMenu();
        ConfigureElevationBanner();
        _ = LoadDrivesAndDiagramAsync();
        SecureDeletionCheck.Checked += (_, _) => UpdateSecureWipePresetState();
        SecureDeletionCheck.Unchecked += (_, _) => UpdateSecureWipePresetState();
        UpdateSecureWipePresetState();
    }

    private void ConfigureElevationBanner()
    {
        ElevationUi.StyleWarningBanner(ElevationBanner);
        ElevationUi.ConfigureBanner(
            ElevationBanner,
            ElevationBannerText,
            "Not running as Administrator. Format, wipe, and partition changes may fail with access errors.",
            RestartAsAdminButton,
            null,
            "Format, free-space overwrite, and partition management need Administrator rights.");
    }

    /// <summary>Applies Mica backdrop on Windows 11; no-op or fallback on older Windows.</summary>
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
            // Mica not supported (e.g. Windows 10); use default window background
        }
    }

    /// <summary>Extends content into the title bar so Mica shows there; sets the drag region.</summary>
    private void TrySetExtendedTitleBar()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
        }
        catch
        {
            // Fall back to default title bar
        }
    }

    /// <summary>Sets the window size (WinUI 3 Window has no Width/Height in XAML).</summary>
    private void TrySetWindowSize(int width, int height)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        }
        catch
        {
            // Ignore if AppWindow API is unavailable
        }
    }

    /// <summary>Presets for allocation unit (cluster) size when formatting.</summary>
    private static readonly (string DisplayName, uint Bytes)[] ClusterSizePresets =
    [
        ("Default (system)", 0),
        ("512 bytes", 512),
        ("1 KB", 1024),
        ("4 KB", 4096),
        ("8 KB", 8192),
        ("16 KB", 16384),
        ("32 KB", 32768),
        ("64 KB", 65536)
    ];

    /// <summary>Populates the allocation unit size preset combo.</summary>
    private void LoadClusterSizePresets()
    {
        ClusterSizePresetCombo.Items.Clear();
        foreach (var (displayName, _) in ClusterSizePresets)
        {
            ClusterSizePresetCombo.Items.Add(displayName);
        }

        ClusterSizePresetCombo.SelectedIndex = 0;
    }

    private void ClusterSizePresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var combo = ClusterSizePresetCombo;
        if (combo == null || combo.SelectedIndex < 0 || combo.SelectedIndex >= ClusterSizePresets.Length)
        {
            return;
        }

        uint bytes = ClusterSizePresets[combo.SelectedIndex].Bytes;
        if (ClusterSizeBox != null)
        {
            ClusterSizeBox.Text = bytes == 0 ? string.Empty : bytes.ToString();
        }
    }

    /// <summary>Populates the free-space overwrite preset combo.</summary>
    private void LoadSecureWipePresets()
    {
        SecureWipePresetCombo.Items.Clear();
        foreach (var (displayName, _) in DriveService.SecureWipePresets)
        {
            SecureWipePresetCombo.Items.Add(displayName);
        }

        SecureWipePresetCombo.SelectedIndex = 0;
    }

    private void UpdateSecureWipePresetState()
    {
        if (SecureWipePresetPanel != null)
        {
            SecureWipePresetPanel.Opacity = SecureDeletionCheck?.IsChecked == true ? 1.0 : 0.7;
        }
    }

    /// <summary>Populates the file system combo; non-native options are disabled.</summary>
    private void LoadFileSystems()
    {
        FileSystemCombo.Items.Clear();
        foreach (var option in DriveService.FormatFileSystems)
        {
            var item = new ComboBoxItem
            {
                Content = option.IsNativelySupported
                    ? option.Name
                    : $"{option.Name} (not supported on Windows)",
                IsEnabled = option.IsNativelySupported,
                Tag = option.Name
            };
            FileSystemCombo.Items.Add(item);
        }

        FileSystemCombo.SelectedIndex = 0;
        UpdateCompressionCheckState();
    }

    private static string? GetSelectedFileSystemName(ComboBox? combo)
    {
        if (combo?.SelectedItem is ComboBoxItem item)
        {
            return item.Tag as string ?? item.Content?.ToString();
        }

        return combo?.SelectedItem?.ToString();
    }

    /// <summary>Enables compression checkbox only when a file system that supports it (NTFS, ReFS) is selected.</summary>
    private void UpdateCompressionCheckState()
    {
        if (CompressionCheck == null)
        {
            return;
        }

        string? fs = GetSelectedFileSystemName(FileSystemCombo);
        bool supported = fs is "NTFS" or "ReFS";
        CompressionCheck.IsEnabled = supported;
        if (!supported && CompressionCheck.IsChecked == true)
        {
            CompressionCheck.IsChecked = false;
        }
    }

    private void FileSystemCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCompressionCheckState();
    }

    /// <summary>Loads drives and disk layout on thread-pool threads, then updates UI on the dispatcher.</summary>
    private async Task LoadDrivesAndDiagramAsync()
    {
        IReadOnlyList<DriveItem> drives;
        IReadOnlyList<DiskInfo> disks;
        try
        {
            var drivesTask = DriveService.GetFormattableDrivesAsync();
            var disksTask = DriveService.GetDiskPartitionLayoutAsync();
            await Task.WhenAll(drivesTask, disksTask).ConfigureAwait(false);
            drives = await drivesTask.ConfigureAwait(false);
            disks = await disksTask.ConfigureAwait(false);
        }
        catch
        {
            drives = [];
            disks = [];
        }

        var queue = DispatcherQueue;
        if (queue == null)
        {
            return;
        }

        queue.TryEnqueue(() =>
        {
            _drives = drives;
            _lastDisks = disks;
            DriveCombo.Items.Clear();
            foreach (DriveItem d in _drives)
            {
                DriveCombo.Items.Add(d);
            }

            if (_drives.Count > 0)
            {
                DriveCombo.SelectedIndex = 0;
            }
            else
            {
                DriveCombo.PlaceholderText = "No formattable drives found (protected volumes excluded).";
                if (!ElevationHelper.IsElevated)
                {
                    ShowStatus("No drives listed. If this looks wrong, restart as Administrator — WMI enumeration can fail without elevation.", isError: true);
                }
            }

            BuildDiagram(disks);
        });
    }

    /// <summary>Refreshes the list of formattable drives (excluding protected volumes) and updates the drive combo.</summary>
    private void RefreshDrives()
    {
        _drives = DriveService.GetFormattableDrives();
        DriveCombo.Items.Clear();
        foreach (DriveItem d in _drives)
        {
            DriveCombo.Items.Add(d);
        }

        if (_drives.Count > 0)
        {
            DriveCombo.SelectedIndex = 0;
        }
        else
        {
            DriveCombo.PlaceholderText = "No formattable drives found (protected volumes excluded).";
        }
    }

    private void DriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DriveLetterBox == null)
        {
            return;
        }

        if (DriveCombo.SelectedItem is DriveItem selected)
        {
            DriveLetterBox.Text = selected.DriveLetter.TrimEnd(':');
        }
        else
        {
            DriveLetterBox.Text = string.Empty;
        }
    }

    private void DriveLetterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Optional: sync combo when user types a letter that matches a listed drive
    }

    /// <summary>Shows a confirmation dialog before formatting. User must check a box to proceed. Returns true if the user confirms.</summary>
    private async Task<bool> ShowFormatConfirmDialogAsync(string driveLetter, string fileSystem, bool secureDeletion, int wipePasses)
    {
        var message = $"You are about to format drive {driveLetter} as {fileSystem}.\n\n"
            + "All data on this drive will be permanently erased. This cannot be undone."
            + (secureDeletion
                ? $"\n\nFree-space overwrite ({wipePasses} pass(es)) will run first. This overwrites unused space only — it is not hardware Secure Erase / NVMe Sanitize."
                : "");

        var confirmCheckBox = new CheckBox
        {
            Content = "I understand that all data will be permanently erased and I want to proceed"
        };
        var errorText = new TextBlock
        {
            Text = "Please check the box above to confirm.",
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 0, 0)),
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap
        };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(confirmCheckBox);
        panel.Children.Add(errorText);

        var dialog = new ContentDialog
        {
            Title = "Confirm format",
            Content = panel,
            PrimaryButtonText = "Format",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };

        dialog.Closing += (s, e) =>
        {
            if (e.Result == ContentDialogResult.Primary && confirmCheckBox.IsChecked != true)
            {
                e.Cancel = true;
                errorText.Visibility = Visibility.Visible;
            }
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        HideStatus();
        _ = LoadDrivesAndDiagramAsync();
    }

    /// <summary>Clears cached diagram brushes so the next BuildDiagram uses current AppSettings colours (e.g. after Settings save).</summary>
    private void InvalidateDiagramColourCache()
    {
        _cachedFormattableHex = null;
        _cachedSystemHex = null;
        _cachedNoLetterHex = null;
    }

    /// <summary>Builds the interactive disk/partition diagram. If <paramref name="disks"/> is null, fetches layout on the current thread.</summary>
    private void BuildDiagram(IReadOnlyList<DiskInfo>? disks = null)
    {
        if (DiagramPanel == null)
        {
            return;
        }

        DiagramPanel.Children.Clear();

        disks ??= DriveService.GetDiskPartitionLayout();
        const double barHeight = 28;

        string formattableHex = AppSettings.GetFormattableColour();
        string systemHex = AppSettings.GetSystemDriveColour();
        string noLetterHex = AppSettings.GetNoLetterColour();
        if (_cachedFormattableHex != formattableHex || _cachedSystemHex != systemHex || _cachedNoLetterHex != noLetterHex)
        {
            _cachedFormattableHex = formattableHex;
            _cachedSystemHex = systemHex;
            _cachedNoLetterHex = noLetterHex;
            Windows.UI.Color fc = AppSettings.ParseColor(formattableHex) ?? Colors.SteelBlue;
            Windows.UI.Color sc = AppSettings.ParseColor(systemHex) ?? Colors.OrangeRed;
            Windows.UI.Color nc = AppSettings.ParseColor(noLetterHex) ?? Colors.Gray;
            _cachedFormattableBrush = new SolidColorBrush(fc);
            _cachedSystemBrush = new SolidColorBrush(sc);
            _cachedNoLetterBrush = new SolidColorBrush(nc);
            _cachedWhiteBrush = new SolidColorBrush(Colors.White);
        }
        var formattableBrush = _cachedFormattableBrush!;
        var systemBrush = _cachedSystemBrush!;
        var noLetterBrush = _cachedNoLetterBrush!;
        var whiteBrush = _cachedWhiteBrush!;

        foreach (DiskInfo disk in disks)
        {
            var row = new StackPanel { Spacing = 4 };
            var caption = new TextBlock
            {
                Text = disk.DisplayCaption,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            row.Children.Add(caption);

            if (disk.Partitions.Count == 0)
            {
                var noPart = new TextBlock { Text = "No partitions", Opacity = 0.7, FontSize = 11 };
                row.Children.Add(noPart);
            }
            else
            {
                var barGrid = new Grid { Height = barHeight, MinWidth = 200 };
                foreach (var t in disk.Partitions)
                {
                    barGrid.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(Math.Max(t.Size, 1), GridUnitType.Star)
                    });
                }
                for (int i = 0; i < disk.Partitions.Count; i++)
                {
                    PartitionInfo part = disk.Partitions[i];
                    var blockBrush = part.IsProtected ? systemBrush : string.IsNullOrEmpty(part.DriveLetter) ? noLetterBrush : formattableBrush;
                    var block = new Button
                    {
                        Content = new TextBlock
                        {
                            Text = part.DisplayLabel,
                            FontSize = 11,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            TextWrapping = TextWrapping.NoWrap
                        },
                        Tag = part,
                        Margin = new Thickness(1, 1, 1, 1),
                        Padding = new Thickness(6, 2, 6, 2),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Background = blockBrush,
                        Foreground = whiteBrush
                    };
                    ToolTipService.SetToolTip(block, part.GetTooltipText(disk.Status));
                    block.Click += PartitionBlock_Click;
                    block.ContextFlyout = CreatePartitionContextMenu(part);
                    Grid.SetColumn(block, i);
                    barGrid.Children.Add(block);
                }
                row.Children.Add(barGrid);
            }
            DiagramPanel.Children.Add(row);
        }
    }

    /// <summary>Creates the context menu for a partition block (format, change letter, Disk Management).</summary>
    private MenuFlyout CreatePartitionContextMenu(PartitionInfo part)
    {
        var formatItem = new MenuFlyoutItem { Text = "Format drive", Tag = part };
        formatItem.Click += ContextMenu_Format_Click;
        formatItem.IsEnabled = !part.IsProtected && !string.IsNullOrEmpty(part.DriveLetter);

        var changeLetterItem = new MenuFlyoutItem { Text = "Change drive letter...", Tag = part };
        changeLetterItem.Click += ContextMenu_ChangeDriveLetter_Click;
        changeLetterItem.IsEnabled = !string.IsNullOrEmpty(part.DriveLetter) && !part.IsProtected;

        var openPartitionManagerItem = new MenuFlyoutItem { Text = "Open Partition Manager", Tag = part };
        openPartitionManagerItem.Click += ContextMenu_OpenPartitionManager_Click;

        var openDiskMgmtItem = new MenuFlyoutItem { Text = "Open Disk Management", Tag = part };
        openDiskMgmtItem.Click += ContextMenu_OpenDiskManagement_Click;

        var flyout = new MenuFlyout();
        flyout.Items.Add(formatItem);
        flyout.Items.Add(changeLetterItem);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(openPartitionManagerItem);
        flyout.Items.Add(openDiskMgmtItem);
        return flyout;
    }

    /// <summary>Sets the context menu on the drive layout section (right-click graph area for Partition Manager).</summary>
    private void SetDriveLayoutContextMenu()
    {
        var diagramMenu = new MenuFlyout();
        var openItem = new MenuFlyoutItem { Text = "Open Partition Manager" };
        openItem.Click += (_, _) =>
        {
            var partitionWindow = new PartitionManagementWindow();
            partitionWindow.Closed += (_, _) => _ = LoadDrivesAndDiagramAsync();
            partitionWindow.Activate();
        };
        diagramMenu.Items.Add(openItem);
        var section = (Content as FrameworkElement)?.FindName("DriveLayoutSection") as StackPanel;
        if (section != null)
        {
            section.ContextFlyout = diagramMenu;
        }
    }

    private void ContextMenu_Format_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not PartitionInfo part)
        {
            return;
        }

        if (part.IsProtected || string.IsNullOrEmpty(part.DriveLetter))
        {
            return;
        }

        if (DriveLetterBox != null)
        {
            DriveLetterBox.Text = part.DriveLetter.TrimEnd(':');
        }

        if (DriveCombo != null)
        {
            for (int i = 0; i < DriveCombo.Items.Count; i++)
            {
                if (DriveCombo.Items[i] is DriveItem d && d.DriveLetter.Equals(part.DriveLetter, StringComparison.OrdinalIgnoreCase))
                {
                    DriveCombo.SelectedIndex = i;
                    break;
                }
            }
        }
        FormatButton_Click(FormatButton!, e);
    }

    private async void ContextMenu_ChangeDriveLetter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not PartitionInfo part)
        {
            return;
        }

        if (string.IsNullOrEmpty(part.DriveLetter) || part.IsProtected)
        {
            return;
        }

        string currentLetter = part.DriveLetter.TrimEnd(':');
        var usedLetters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var disk in _lastDisks)
        {
            foreach (var p in disk.Partitions)
            {
                if (!string.IsNullOrEmpty(p.DriveLetter))
                {
                    usedLetters.Add(p.DriveLetter.TrimEnd(':'));
                }
            }
        }
        var available = new List<string>();
        for (char c = 'A'; c <= 'Z'; c++)
        {
            string letter = c.ToString();
            if (!usedLetters.Contains(letter))
            {
                available.Add(letter + ":");
            }
        }
        if (available.Count == 0)
        {
            ShowStatus("No free drive letters available.", isError: true);
            return;
        }
        var combo = new ComboBox
        {
            ItemsSource = available,
            SelectedIndex = 0,
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = $"New drive letter for {part.DriveLetter}:", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(combo);
        var dialog = new ContentDialog
        {
            Title = "Change drive letter",
            Content = panel,
            PrimaryButtonText = "Assign",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        string newLetter = (combo.SelectedItem as string) ?? available[0];
        newLetter = newLetter.TrimEnd(':');
        bool ok = DriveService.TryAssignDriveLetter(currentLetter, newLetter);
        if (ok)
        {
            ShowStatus($"Drive letter changed to {newLetter}:. Refreshing.", isError: false);
            _ = LoadDrivesAndDiagramAsync();
        }
        else
        {
            ShowStatus("Could not change drive letter. Try running as administrator or use Disk Management.", isError: true);
        }
    }

    private void ContextMenu_OpenPartitionManager_Click(object sender, RoutedEventArgs e)
    {
        var partitionWindow = new PartitionManagementWindow();
        partitionWindow.Closed += (_, _) => _ = LoadDrivesAndDiagramAsync();
        partitionWindow.Activate();
    }

    private void ContextMenu_OpenDiskManagement_Click(object sender, RoutedEventArgs e)
    {
        DriveService.OpenDiskManagement();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(onSaved: () =>
        {
            InvalidateDiagramColourCache();
            _ = LoadDrivesAndDiagramAsync();
        });
        settingsWindow.Activate();
    }

    private void ManagePartitionsButton_Click(object sender, RoutedEventArgs e)
    {
        var partitionWindow = new PartitionManagementWindow();
        partitionWindow.Closed += (_, _) => _ = LoadDrivesAndDiagramAsync();
        partitionWindow.Activate();
    }

    private void PartitionBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not PartitionInfo part)
        {
            return;
        }

        if (DriveLetterBox != null)
        {
            DriveLetterBox.Text = string.IsNullOrEmpty(part.DriveLetter) ? string.Empty : part.DriveLetter.TrimEnd(':');
        }

        if (!string.IsNullOrEmpty(part.DriveLetter) && DriveCombo != null)
        {
            for (int i = 0; i < DriveCombo.Items.Count; i++)
            {
                if (DriveCombo.Items[i] is DriveItem d && d.DriveLetter.Equals(part.DriveLetter, StringComparison.OrdinalIgnoreCase))
                {
                    DriveCombo.SelectedIndex = i;
                    break;
                }
            }
        }
    }

    /// <summary>Resolves the drive letter to use: from DriveLetterBox if valid, otherwise from the selected drive.</summary>
    private static string NormalizeDriveLetter(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        char c = input.Trim().ToUpperInvariant()[0];
        if (c is >= 'A' and <= 'Z')
        {
            return c + ":";
        }

        return string.Empty;
    }

    private async void FormatButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ElevationHelper.IsElevated && Content?.XamlRoot is { } root)
        {
            bool abort = await ElevationUi.EnsureElevatedOrAbortAsync(
                root,
                "Format and free-space overwrite need Administrator rights.",
                requireElevation: true).ConfigureAwait(true);
            if (abort)
            {
                return;
            }
        }

        string driveLetter = NormalizeDriveLetter(DriveLetterBox?.Text?.Trim());
        if (string.IsNullOrEmpty(driveLetter))
        {
            if (DriveCombo.SelectedItem is DriveItem selected)
            {
                driveLetter = selected.DriveLetter.TrimEnd(':').ToUpperInvariant() + ":";
            }
            else
            {
                ShowStatus("Please select a drive or enter a drive letter (e.g. D).", isError: true);
                return;
            }
        }

        if (DriveService.IsProtectedDriveLetter(driveLetter))
        {
            ShowStatus("Protected volumes (system, boot, EFI, recovery) cannot be formatted.", isError: true);
            return;
        }

        string fileSystem = GetSelectedFileSystemName(FileSystemCombo) ?? "NTFS";
        if (!DriveService.FormatFileSystems.Any(f => f.IsNativelySupported &&
            f.Name.Equals(fileSystem, StringComparison.OrdinalIgnoreCase)))
        {
            ShowStatus("Select a Windows-supported file system (NTFS, FAT32, FAT, exFAT, or ReFS).", isError: true);
            return;
        }

        string label = VolumeLabelBox?.Text?.Trim() ?? string.Empty;
        uint clusterSize = 0;
        if (!string.IsNullOrWhiteSpace(ClusterSizeBox?.Text) && uint.TryParse(ClusterSizeBox.Text.Trim(), out uint parsed))
        {
            clusterSize = parsed;
        }

        bool quick = QuickFormatCheck?.IsChecked == true;
        bool compression = CompressionCheck?.IsChecked == true;
        bool secureDeletion = SecureDeletionCheck?.IsChecked == true;
        int wipePasses = 1;
        if (secureDeletion && SecureWipePresetCombo?.SelectedIndex >= 0 && SecureWipePresetCombo.SelectedIndex < DriveService.SecureWipePresets.Length)
        {
            wipePasses = DriveService.SecureWipePresets[SecureWipePresetCombo.SelectedIndex].Passes;
        }

        bool confirmed = await ShowFormatConfirmDialogAsync(driveLetter, fileSystem, secureDeletion, wipePasses);
        if (!confirmed)
        {
            return;
        }

        SetBusy(true);
        HideStatus();

        try
        {
            if (secureDeletion && wipePasses > 0)
            {
                ShowProgressBar(determinate: true);
                var progress = new Progress<(int pass, int totalPasses, double percent)>(p =>
                {
                    if (ProgressBar != null)
                    {
                        ProgressBar.Value = p.percent;
                    }

                    if (ProgressLabel != null)
                    {
                        ProgressLabel.Text = $"Free-space overwrite: pass {p.pass}/{p.totalPasses} ({p.percent:F0}%)";
                        ProgressLabel.Visibility = Visibility.Visible;
                    }
                });
                bool wiped = await DriveService.SecureWipeAsync(driveLetter, wipePasses, progress);
                ShowProgressBar(determinate: false);
                if (ProgressLabel != null)
                {
                    ProgressLabel.Visibility = Visibility.Collapsed;
                }

                if (!wiped)
                {
                    ShowStatus("Free-space overwrite failed or was skipped (e.g. protected drive).", isError: true);
                    return;
                }
            }

            uint result = await DriveService.FormatAsync(driveLetter, fileSystem, quick, clusterSize, label, compression);
            string message = DriveService.GetFormatResultMessage(result);
            if (result == 0)
            {
                ShowStatus(secureDeletion ? "Free-space overwrite and format completed." : message, isError: false);
                _ = LoadDrivesAndDiagramAsync();
            }
            else
            {
                ShowStatus(message, isError: true);
            }
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Operation canceled.", isError: true);
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}", isError: true);
        }
        finally
        {
            SetBusy(false);
            ShowProgressBar(determinate: false);
            if (ProgressLabel != null)
            {
                ProgressLabel.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void ShowProgressBar(bool determinate)
    {
        if (ProgressBar == null)
        {
            return;
        }

        ProgressBar.IsIndeterminate = !determinate;
        ProgressBar.Visibility = Visibility.Visible;
        if (!determinate)
        {
            ProgressBar.Value = 0;
        }
    }

    private void SetBusy(bool busy)
    {
        if (ProgressBar != null)
        {
            ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        if (FormatButton != null)
        {
            FormatButton.IsEnabled = !busy;
        }

        if (RefreshButton != null)
        {
            RefreshButton.IsEnabled = !busy;
        }

        if (DriveCombo != null)
        {
            DriveCombo.IsEnabled = !busy;
        }

        if (SecureDeletionCheck != null)
        {
            SecureDeletionCheck.IsEnabled = !busy;
        }

        if (SecureWipePresetCombo != null)
        {
            SecureWipePresetCombo.IsEnabled = !busy;
        }
    }

    private void ShowStatus(string text, bool isError)
    {
        if (StatusText == null)
        {
            return;
        }

        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
        // Keep default critical brush for errors; could switch to success brush when !isError
    }

    private void HideStatus()
    {
        if (StatusText != null)
        {
            StatusText.Visibility = Visibility.Collapsed;
        }
    }
}
