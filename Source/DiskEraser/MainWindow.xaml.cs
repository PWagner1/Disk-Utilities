using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using Microsoft.UI.Xaml.Media;
using DiskEraser.Models;
using DiskEraser.Services;

namespace DiskEraser;

/// <summary>
/// Main window for the Disk Eraser app: lists formattable drives, format options, and runs format via <see cref="DriveService"/>.
/// The system drive (C:) is never shown and cannot be formatted.
/// </summary>
public sealed partial class MainWindow : Window
{
    private IReadOnlyList<DriveItem> _drives = Array.Empty<DriveItem>();

    public MainWindow()
    {
        InitializeComponent();
        TrySetMicaBackdrop();
        TrySetExtendedTitleBar();
        TrySetWindowSize(1500, 800);
        LoadFileSystems();
        LoadClusterSizePresets();
        LoadSecureWipePresets();
        RefreshDrives();
        BuildDiagram();
        SetDriveLayoutContextMenu();
        SecureDeletionCheck.Checked += (_, _) => UpdateSecureWipePresetState();
        SecureDeletionCheck.Unchecked += (_, _) => UpdateSecureWipePresetState();
        UpdateSecureWipePresetState();
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
    {
        ("Default (system)", 0),
        ("512 bytes", 512),
        ("1 KB", 1024),
        ("4 KB", 4096),
        ("8 KB", 8192),
        ("16 KB", 16384),
        ("32 KB", 32768),
        ("64 KB", 65536),
    };

    /// <summary>Populates the allocation unit size preset combo.</summary>
    private void LoadClusterSizePresets()
    {
        ClusterSizePresetCombo.Items.Clear();
        foreach (var (displayName, _) in ClusterSizePresets)
            ClusterSizePresetCombo.Items.Add(displayName);
        ClusterSizePresetCombo.SelectedIndex = 0;
    }

    private void ClusterSizePresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClusterSizePresetCombo?.SelectedIndex < 0 || ClusterSizePresetCombo.SelectedIndex >= ClusterSizePresets.Length)
        {
            return;
        }

        uint bytes = ClusterSizePresets[ClusterSizePresetCombo.SelectedIndex].Bytes;
        if (ClusterSizeBox != null)
        {
            ClusterSizeBox.Text = bytes == 0 ? string.Empty : bytes.ToString();
        }
    }

    /// <summary>Populates the secure wipe preset combo (1 pass, DoD 3-pass, Military grade 7-pass).</summary>
    private void LoadSecureWipePresets()
    {
        SecureWipePresetCombo.Items.Clear();
        foreach (var (displayName, _) in DriveService.SecureWipePresets)
            SecureWipePresetCombo.Items.Add(displayName);
        SecureWipePresetCombo.SelectedIndex = 0;
    }

    private void UpdateSecureWipePresetState()
    {
        if (SecureWipePresetPanel != null)
        {
            SecureWipePresetPanel.Opacity = SecureDeletionCheck?.IsChecked == true ? 1.0 : 0.7;
        }
    }

    /// <summary>Populates the file system combo with all supported file systems.</summary>
    private void LoadFileSystems()
    {
        FileSystemCombo.Items.Clear();
        foreach (string fs in DriveService.SupportedFileSystems)
            FileSystemCombo.Items.Add(fs);
        FileSystemCombo.SelectedIndex = 0;
    }

    /// <summary>Refreshes the list of formattable drives (excluding C:) and updates the drive combo.</summary>
    private void RefreshDrives()
    {
        _drives = DriveService.GetFormattableDrives();
        DriveCombo.Items.Clear();
        foreach (DriveItem d in _drives)
            DriveCombo.Items.Add(d);
        if (_drives.Count > 0)
        {
            DriveCombo.SelectedIndex = 0;
        }
        else
        {
            DriveCombo.PlaceholderText = "No other drives found (C: is protected).";
        }
    }

    private void DriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DriveLetterBox == null)
            return;
        if (DriveCombo.SelectedItem is DriveItem selected)
            DriveLetterBox.Text = selected.DriveLetter.TrimEnd(':');
        else
            DriveLetterBox.Text = string.Empty;
    }

    private void DriveLetterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Optional: sync combo when user types a letter that matches a listed drive
    }

    /// <summary>Shows a confirmation dialog before formatting. Returns true if the user confirms.</summary>
    private async Task<bool> ShowFormatConfirmDialogAsync(string driveLetter, string fileSystem, bool secureDeletion, int wipePasses)
    {
        var message = $"You are about to format drive {driveLetter} as {fileSystem}.\n\n"
            + "All data on this drive will be lost."
            + (secureDeletion ? $"\n\nSecure deletion ({wipePasses} pass(es)) will run first." : "")
            + "\n\nDo you want to continue?";
        var dialog = new ContentDialog
        {
            Title = "Confirm format",
            Content = message,
            PrimaryButtonText = "Format",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        HideStatus();
        RefreshDrives();
        BuildDiagram();
    }

    /// <summary>Builds the interactive disk/partition diagram and wires partition click to drive selection.</summary>
    private void BuildDiagram()
    {
        if (DiagramPanel == null) return;
        DiagramPanel.Children.Clear();

        IReadOnlyList<DiskInfo> disks = DriveService.GetDiskPartitionLayout();
        const double barHeight = 28;

        Windows.UI.Color formattableColour = AppSettings.ParseColor(AppSettings.GetFormattableColour()) ?? Colors.SteelBlue;
        Windows.UI.Color systemColour = AppSettings.ParseColor(AppSettings.GetSystemDriveColour()) ?? Colors.OrangeRed;
        Windows.UI.Color noLetterColour = AppSettings.ParseColor(AppSettings.GetNoLetterColour()) ?? Colors.Gray;
        var formattableBrush = new SolidColorBrush(formattableColour);
        var systemBrush = new SolidColorBrush(systemColour);
        var noLetterBrush = new SolidColorBrush(noLetterColour);
        var whiteBrush = new SolidColorBrush(Colors.White);

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
                for (int i = 0; i < disk.Partitions.Count; i++)
                {
                    barGrid.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(Math.Max(disk.Partitions[i].Size, 1), GridUnitType.Star)
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
                    ToolTipService.SetToolTip(block, $"{part.DisplayLabel} — {part.FormattedSize} — {part.FileSystem}");
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

    /// <summary>Sets the context menu on the drive layout section (right-click graph area for Partition Manager).</summary>
    private void SetDriveLayoutContextMenu()
    {
        var diagramMenu = new MenuFlyout();
        var openItem = new MenuFlyoutItem { Text = "Open Partition Manager" };
        openItem.Click += (_, _) =>
        {
            var partitionWindow = new PartitionManagementWindow();
            partitionWindow.Closed += (_, _) =>
            {
                RefreshDrives();
                BuildDiagram();
            };
            partitionWindow.Activate();
        };
        diagramMenu.Items.Add(openItem);
        var section = (Content as FrameworkElement)?.FindName("DriveLayoutSection") as StackPanel;
        if (section != null)
            section.ContextFlyout = diagramMenu;
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

    private void ContextMenu_Format_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not PartitionInfo part)
            return;
        if (part.IsProtected || string.IsNullOrEmpty(part.DriveLetter)) return;
        if (DriveLetterBox != null) DriveLetterBox.Text = part.DriveLetter.TrimEnd(':');
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
            return;
        if (string.IsNullOrEmpty(part.DriveLetter) || part.IsProtected) return;
        string currentLetter = part.DriveLetter.TrimEnd(':');
        var usedLetters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var disk in DriveService.GetDiskPartitionLayout())
        {
            foreach (var p in disk.Partitions)
            {
                if (!string.IsNullOrEmpty(p.DriveLetter))
                    usedLetters.Add(p.DriveLetter.TrimEnd(':'));
            }
        }
        var available = new List<string>();
        for (char c = 'A'; c <= 'Z'; c++)
        {
            string letter = c.ToString();
            if (!usedLetters.Contains(letter))
                available.Add(letter + ":");
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
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        string newLetter = (combo.SelectedItem as string) ?? available[0];
        newLetter = newLetter.TrimEnd(':');
        bool ok = DriveService.TryAssignDriveLetter(currentLetter, newLetter);
        if (ok)
        {
            ShowStatus($"Drive letter changed to {newLetter}:. Refreshing.", isError: false);
            RefreshDrives();
            BuildDiagram();
        }
        else
        {
            ShowStatus("Could not change drive letter. Try running as administrator or use Disk Management.", isError: true);
        }
    }

    private void ContextMenu_OpenPartitionManager_Click(object sender, RoutedEventArgs e)
    {
        var partitionWindow = new PartitionManagementWindow();
        partitionWindow.Closed += (_, _) =>
        {
            RefreshDrives();
            BuildDiagram();
        };
        partitionWindow.Activate();
    }

    private void ContextMenu_OpenDiskManagement_Click(object sender, RoutedEventArgs e)
    {
        DriveService.OpenDiskManagement();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(onSaved: () => BuildDiagram());
        settingsWindow.Activate();
    }

    private void ManagePartitionsButton_Click(object sender, RoutedEventArgs e)
    {
        var partitionWindow = new PartitionManagementWindow();
        partitionWindow.Closed += (_, _) =>
        {
            RefreshDrives();
            BuildDiagram();
        };
        partitionWindow.Activate();
    }

    private void PartitionBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not PartitionInfo part)
            return;
        if (DriveLetterBox != null)
            DriveLetterBox.Text = string.IsNullOrEmpty(part.DriveLetter) ? string.Empty : part.DriveLetter.TrimEnd(':');
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
            return string.Empty;
        char c = input.Trim().ToUpperInvariant()[0];
        if (c >= 'A' && c <= 'Z')
            return c + ":";
        return string.Empty;
    }

    private async void FormatButton_Click(object sender, RoutedEventArgs e)
    {
        string driveLetter = NormalizeDriveLetter(DriveLetterBox?.Text?.Trim());
        if (string.IsNullOrEmpty(driveLetter))
        {
            if (DriveCombo.SelectedItem is DriveItem selected)
                driveLetter = selected.DriveLetter.TrimEnd(':').ToUpperInvariant() + ":";
            else
            {
                ShowStatus("Please select a drive or enter a drive letter (e.g. D).", isError: true);
                return;
            }
        }

        if (driveLetter.Equals(DriveService.ProtectedDriveLetter, StringComparison.Ordinal))
        {
            ShowStatus("The system drive (C:) cannot be formatted.", isError: true);
            return;
        }

        string fileSystem = FileSystemCombo.SelectedItem?.ToString() ?? "NTFS";
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
            return;

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
                        ProgressLabel.Text = $"Secure wipe: pass {p.pass}/{p.totalPasses} ({p.percent:F0}%)";
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
                    ShowStatus("Secure wipe failed or was skipped (e.g. protected drive).", isError: true);
                    return;
                }
            }

            uint result = await DriveService.FormatAsync(driveLetter, fileSystem, quick, clusterSize, label, compression);
            string message = DriveService.GetFormatResultMessage(result);
            if (result == 0)
            {
                ShowStatus(secureDeletion ? "Secure wipe and format completed." : message, isError: false);
                RefreshDrives();
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
