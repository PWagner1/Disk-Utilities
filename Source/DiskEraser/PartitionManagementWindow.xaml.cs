using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Microsoft.UI.Composition.SystemBackdrops;
using DiskEraser.Models;
using DiskEraser.Services;

namespace DiskEraser;

/// <summary>
/// Window for managing partitions: create, delete, format, assign letter, extend, shrink.
/// Uses diskpart for most operations; may require administrator rights.
/// </summary>
public sealed partial class PartitionManagementWindow : Window
{
    private IReadOnlyList<DiskInfo> _disks = Array.Empty<DiskInfo>();
    private readonly List<PartitionListEntry> _partitionEntries = [];

    public PartitionManagementWindow()
    {
        InitializeComponent();
        TrySetMicaBackdrop();
        TrySetExtendedTitleBar();
        TrySetWindowSize(782, 480);
        RefreshDisks();
    }

    private void TrySetMicaBackdrop()
    {
        try
        {
            if (MicaController.IsSupported())
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        }
        catch { /* ignore */ }
    }

    private void TrySetExtendedTitleBar()
    {
        try
        {
            var titleBar = (Content as FrameworkElement)?.FindName("PartitionTitleBar") as Grid;
            if (titleBar != null)
            {
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(titleBar);
            }
        }
        catch { /* ignore */ }
    }

    private void TrySetWindowSize(int width, int height)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow.GetFromWindowId(windowId).Resize(new Windows.Graphics.SizeInt32(width, height));
        }
        catch { /* ignore */ }
    }

    private void RefreshDisks()
    {
        _disks = DriveService.GetDiskPartitionLayout();
        DiskCombo.Items.Clear();
        foreach (var d in _disks)
            DiskCombo.Items.Add(d);
        if (_disks.Count > 0)
            DiskCombo.SelectedIndex = 0;
        else
            PartitionList.ItemsSource = null;
    }

    private void DiskCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _partitionEntries.Clear();
        if (DiskCombo.SelectedItem is not DiskInfo disk)
        {
            PartitionList.ItemsSource = null;
            UpdateButtonStates(null);
            if (PieChartCanvas != null) PieChartCanvas.Children.Clear();
            return;
        }
        ulong used = 0;
        foreach (var p in disk.Partitions)
        {
            used += p.Size;
            _partitionEntries.Add(new PartitionListEntry
            {
                DisplayText = $"{p.DisplayLabel} — {p.FormattedSize} — {p.FileSystem}",
                IsUnallocated = false,
                Partition = p,
                DiskIndex = disk.DiskIndex
            });
        }
        ulong unallocated = disk.Size > used ? disk.Size - used : 0;
        if (unallocated > 0)
        {
            _partitionEntries.Add(new PartitionListEntry
            {
                DisplayText = $"Unallocated — {FormatBytes(unallocated)}",
                IsUnallocated = true,
                Unallocated = new UnallocatedInfo { DiskIndex = disk.DiskIndex, Size = unallocated },
                DiskIndex = disk.DiskIndex
            });
        }
        PartitionList.ItemsSource = _partitionEntries.ToList();
        PartitionList.SelectedIndex = -1;
        UpdateButtonStates(null);
        BuildPieChart(disk);
    }

    /// <summary>Builds a 3D-style (elliptical) pie chart for the selected disk on PieChartCanvas.</summary>
    private void BuildPieChart(DiskInfo disk)
    {
        if (PieChartCanvas == null) return;
        PieChartCanvas.Children.Clear();

        ulong total = disk.Size > 0 ? disk.Size : 1;
        double cx = 110;
        double cy = 80;
        double rx = 100;
        double ry = 58; // Elliptical for 3D tilt effect
        double startAngleDeg = 0; // Start from right (3 o'clock)

        // Colours: use AppSettings for partition types, gray for unallocated
        Windows.UI.Color formattable = AppSettings.ParseColor(AppSettings.GetFormattableColour()) ?? Microsoft.UI.Colors.SteelBlue;
        Windows.UI.Color systemDrive = AppSettings.ParseColor(AppSettings.GetSystemDriveColour()) ?? Microsoft.UI.Colors.OrangeRed;
        Windows.UI.Color noLetter = AppSettings.ParseColor(AppSettings.GetNoLetterColour()) ?? Microsoft.UI.Colors.Gray;
        Windows.UI.Color unallocated = Microsoft.UI.Colors.DarkGray;

        foreach (var p in disk.Partitions)
        {
            double sweepDeg = 360.0 * (p.Size / (double)total);
            if (sweepDeg <= 0) continue;
            Windows.UI.Color fill = p.IsProtected ? systemDrive : string.IsNullOrEmpty(p.DriveLetter) ? noLetter : formattable;
            AddPieSlice(PieChartCanvas, cx, cy, rx, ry, startAngleDeg, sweepDeg, fill);
            startAngleDeg += sweepDeg;
        }

        ulong used = disk.Partitions.Aggregate(0UL, (s, p) => s + p.Size);
        ulong free = disk.Size > used ? disk.Size - used : 0;
        if (free > 0)
        {
            double sweepDeg = 360.0 * (free / (double)total);
            AddPieSlice(PieChartCanvas, cx, cy, rx, ry, startAngleDeg, sweepDeg, unallocated);
        }
    }

    private static void AddPieSlice(Canvas canvas, double cx, double cy, double rx, double ry, double startAngleDeg, double sweepDeg, Windows.UI.Color color)
    {
        double startRad = startAngleDeg * Math.PI / 180.0;
        double endRad = (startAngleDeg + sweepDeg) * Math.PI / 180.0;
        double x1 = cx + rx * Math.Cos(startRad);
        double y1 = cy + ry * Math.Sin(startRad);
        double x2 = cx + rx * Math.Cos(endRad);
        double y2 = cy + ry * Math.Sin(endRad);

        var figure = new PathFigure
        {
            StartPoint = new Windows.Foundation.Point(cx, cy),
            IsClosed = true
        };
        figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(x1, y1) });
        figure.Segments.Add(new ArcSegment
        {
            Point = new Windows.Foundation.Point(x2, y2),
            Size = new Windows.Foundation.Size(rx, ry),
            IsLargeArc = sweepDeg > 180,
            SweepDirection = SweepDirection.Clockwise
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        var pathShape = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geometry,
            Fill = new SolidColorBrush(color),
            Stroke = new SolidColorBrush(Microsoft.UI.Colors.Black),
            StrokeThickness = 1
        };
        Canvas.SetLeft(pathShape, 0);
        Canvas.SetTop(pathShape, 0);
        canvas.Children.Add(pathShape);
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int o = 0;
        double len = bytes;
        while (len >= 1024 && o < sizes.Length - 1) { o++; len /= 1024; }
        return $"{len:0.##} {sizes[o]}";
    }

    private void PartitionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateButtonStates(PartitionList.SelectedItem as PartitionListEntry);
    }

    private void UpdateButtonStates(PartitionListEntry? entry)
    {
        bool unallocated = entry?.IsUnallocated == true;
        bool partition = entry?.Partition != null;
        var part = entry?.Partition;
        bool hasLetter = !string.IsNullOrEmpty(part?.DriveLetter);
        bool isProtected = part?.IsProtected == true;

        CreatePartitionButton.IsEnabled = unallocated;
        DeletePartitionButton.IsEnabled = partition && !isProtected;
        FormatButton.IsEnabled = partition && hasLetter && !isProtected;
        AssignLetterButton.IsEnabled = partition && !isProtected;
        ExtendButton.IsEnabled = partition && hasLetter && !isProtected;
        ShrinkButton.IsEnabled = partition && hasLetter && !isProtected;
    }

    private void ShowStatus(string text, bool isError = false)
    {
        if (StatusText == null) return;
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
    }

    private void HideStatus()
    {
        if (StatusText != null) StatusText.Visibility = Visibility.Collapsed;
    }

    private async void CreatePartitionButton_Click(object sender, RoutedEventArgs e)
    {
        var entry = PartitionList.SelectedItem as PartitionListEntry;
        if (entry?.IsUnallocated != true || entry.Unallocated == null)
        {
            ShowStatus("Select unallocated space first.", true);
            return;
        }
        var panel = new StackPanel { Spacing = 8 };
        var sizeBox = new TextBox { PlaceholderText = "0 = all space", Width = 200, InputScope = new InputScope() };
        sizeBox.InputScope.Names.Add(new InputScopeName(InputScopeNameValue.Number));
        panel.Children.Add(new TextBlock { Text = "Size in MB (0 = use all unallocated):" });
        panel.Children.Add(sizeBox);
        var dialog = new ContentDialog
        {
            Title = "Create partition",
            Content = panel,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        uint sizeMB = 0;
        if (!string.IsNullOrWhiteSpace(sizeBox.Text) && !uint.TryParse(sizeBox.Text.Trim(), out sizeMB))
            sizeMB = 0;
        HideStatus();
        bool ok = DriveService.CreatePartition(entry.DiskIndex, sizeMB);
        if (ok)
        {
            ShowStatus("Partition created. Refreshing.", false);
            RefreshDisks();
            if (DiskCombo.SelectedItem is DiskInfo d)
                DiskCombo_SelectionChanged(DiskCombo, new SelectionChangedEventArgs(null, null));
        }
        else
            ShowStatus("Create failed. Try running as administrator.", true);
    }

    private async void DeletePartitionButton_Click(object sender, RoutedEventArgs e)
    {
        var entry = PartitionList.SelectedItem as PartitionListEntry;
        if (entry?.Partition == null || entry.Partition.IsProtected)
        {
            ShowStatus("Select a non-system partition to delete.", true);
            return;
        }
        var confirm = new ContentDialog
        {
            Title = "Delete partition",
            Content = $"Delete {entry.Partition.DisplayLabel}? All data will be lost.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        HideStatus();
        bool ok = DriveService.DeletePartition(entry.DiskIndex, entry.Partition.PartitionIndex);
        if (ok)
        {
            ShowStatus("Partition deleted. Refreshing.", false);
            RefreshDisks();
            if (DiskCombo.SelectedItem is DiskInfo d)
                DiskCombo_SelectionChanged(DiskCombo, new SelectionChangedEventArgs(null, null));
        }
        else
            ShowStatus("Delete failed. Try running as administrator.", true);
    }

    private async void FormatButton_Click(object sender, RoutedEventArgs e)
    {
        var entry = PartitionList.SelectedItem as PartitionListEntry;
        if (entry?.Partition == null || string.IsNullOrEmpty(entry.Partition.DriveLetter) || entry.Partition.IsProtected)
            return;
        HideStatus();
        uint result = await DriveService.FormatAsync(entry.Partition.DriveLetter, "NTFS", true, 0, "", false);
        string msg = DriveService.GetFormatResultMessage(result);
        if (result == 0)
        {
            ShowStatus("Format completed.", false);
            RefreshDisks();
            if (DiskCombo.SelectedItem is DiskInfo d)
                DiskCombo_SelectionChanged(DiskCombo, new SelectionChangedEventArgs(null, null));
        }
        else
            ShowStatus(msg, true);
    }

    private async void AssignLetterButton_Click(object sender, RoutedEventArgs e)
    {
        var entry = PartitionList.SelectedItem as PartitionListEntry;
        if (entry?.Partition == null || entry.Partition.IsProtected) return;
        var part = entry.Partition;
        string currentLetter = part.DriveLetter.TrimEnd(':');
        var usedLetters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var disk in DriveService.GetDiskPartitionLayout())
            foreach (var p in disk.Partitions)
                if (!string.IsNullOrEmpty(p.DriveLetter))
                    usedLetters.Add(p.DriveLetter.TrimEnd(':'));
        var available = new List<string>();
        for (char c = 'A'; c <= 'Z'; c++)
        {
            string letter = c.ToString();
            if (!usedLetters.Contains(letter))
                available.Add(letter + ":");
        }
        if (available.Count == 0)
        {
            ShowStatus("No free drive letters.", true);
            return;
        }
        var combo = new ComboBox { ItemsSource = available, SelectedIndex = 0, Width = 120 };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(part.DriveLetter) ? "Assign drive letter:" : "Change drive letter:" });
        panel.Children.Add(combo);
        var dialog = new ContentDialog
        {
            Title = string.IsNullOrEmpty(part.DriveLetter) ? "Assign letter" : "Change letter",
            Content = panel,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        string newLetter = (combo.SelectedItem as string) ?? available[0];
        newLetter = newLetter.TrimEnd(':');
        bool ok;
        if (string.IsNullOrEmpty(part.DriveLetter))
            ok = DriveService.AssignLetterToPartition(entry.DiskIndex, part.PartitionIndex, newLetter);
        else
            ok = DriveService.TryAssignDriveLetter(currentLetter, newLetter);
        HideStatus();
        if (ok)
        {
            ShowStatus($"Drive letter set to {newLetter}:. Refreshing.", false);
            RefreshDisks();
            if (DiskCombo.SelectedItem is DiskInfo d)
                DiskCombo_SelectionChanged(DiskCombo, new SelectionChangedEventArgs(null, null));
        }
        else
            ShowStatus("Failed. Try running as administrator.", true);
    }

    private async void ExtendButton_Click(object sender, RoutedEventArgs e)
    {
        var entry = PartitionList.SelectedItem as PartitionListEntry;
        if (entry?.Partition == null || string.IsNullOrEmpty(entry.Partition.DriveLetter) || entry.Partition.IsProtected)
            return;
        int? volNum = DriveService.GetVolumeNumberForDriveLetter(entry.Partition.DriveLetter);
        if (volNum == null) { ShowStatus("Could not get volume. Run the app as administrator for Extend/Shrink.", true); return; }
        var sizeBox = new TextBox { PlaceholderText = "0 = use all", Width = 200 };
        sizeBox.InputScope = new InputScope();
        sizeBox.InputScope.Names.Add(new InputScopeName(InputScopeNameValue.Number));
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Additional size in MB (0 = use all contiguous unallocated):" });
        panel.Children.Add(sizeBox);
        var dialog = new ContentDialog
        {
            Title = "Extend volume",
            Content = panel,
            PrimaryButtonText = "Extend",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        uint? sizeMB = null;
        if (!string.IsNullOrWhiteSpace(sizeBox.Text) && uint.TryParse(sizeBox.Text.Trim(), out uint s))
            sizeMB = s;
        HideStatus();
        bool ok = DriveService.ExtendVolume(volNum.Value, sizeMB);
        if (ok)
        {
            ShowStatus("Volume extended. Refreshing.", false);
            RefreshDisks();
            if (DiskCombo.SelectedItem is DiskInfo d)
                DiskCombo_SelectionChanged(DiskCombo, new SelectionChangedEventArgs(null, null));
        }
        else
            ShowStatus("Extend failed. Try running as administrator.", true);
    }

    private async void ShrinkButton_Click(object sender, RoutedEventArgs e)
    {
        var entry = PartitionList.SelectedItem as PartitionListEntry;
        if (entry?.Partition == null || string.IsNullOrEmpty(entry.Partition.DriveLetter) || entry.Partition.IsProtected)
            return;
        int? volNum = DriveService.GetVolumeNumberForDriveLetter(entry.Partition.DriveLetter);
        if (volNum == null) { ShowStatus("Could not get volume. Run the app as administrator for Extend/Shrink.", true); return; }
        var sizeBox = new TextBox { PlaceholderText = "MB to shrink by", Width = 200 };
        sizeBox.InputScope = new InputScope();
        sizeBox.InputScope.Names.Add(new InputScopeName(InputScopeNameValue.Number));
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Amount to shrink (MB):" });
        panel.Children.Add(sizeBox);
        var dialog = new ContentDialog
        {
            Title = "Shrink volume",
            Content = panel,
            PrimaryButtonText = "Shrink",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (!uint.TryParse(sizeBox.Text?.Trim(), out uint desiredMB) || desiredMB == 0)
        {
            ShowStatus("Enter a valid size in MB.", true);
            return;
        }
        HideStatus();
        bool ok = DriveService.ShrinkVolume(volNum.Value, desiredMB);
        if (ok)
        {
            ShowStatus("Volume shrunk. Refreshing.", false);
            RefreshDisks();
            if (DiskCombo.SelectedItem is DiskInfo d)
                DiskCombo_SelectionChanged(DiskCombo, new SelectionChangedEventArgs(null, null));
        }
        else
            ShowStatus("Shrink failed. Try running as administrator.", true);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        HideStatus();
        RefreshDisks();
        if (DiskCombo.SelectedItem is DiskInfo d)
            DiskCombo_SelectionChanged(DiskCombo, new SelectionChangedEventArgs(null, null));
    }
}
