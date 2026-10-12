using Windows.Storage;
using Windows.Storage.Pickers;

namespace HandleFinder;

/// <summary>
/// Main window for HandleFinder - find processes locking files or folders.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly HandleService _handleService = new();
    private readonly ObservableCollection<HandleInfo> _handles = [];
    private List<HandleInfo> _allHandles = [];
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _watchCts;

    public MainWindow(string? initialPath = null)
    {
        InitializeComponent();
        WindowChrome.TryApplyMica(this);
        WindowChrome.TryExtendTitleBar(this, AppTitleBar);
        WindowChrome.TrySetSize(this, 980, 680);
        HandlesList.ItemsSource = _handles;
        ConfigureElevationBanner();
        LoadDrives();
        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            PathBox.Text = initialPath.Trim().Trim('"');
        }
    }

    private void ConfigureElevationBanner()
    {
        ElevationUi.StyleWarningBanner(ElevationBanner);
        ElevationUi.ConfigureBanner(
            ElevationBanner,
            ElevationBannerText,
            "Not running as Administrator. Handle enumeration needs elevation to inspect other processes.",
            RestartAsAdminButton,
            null,
            "Finding and closing handles needs Administrator rights (SeDebugPrivilege).");
    }

    private void LoadDrives()
    {
        DriveCombo.Items.Clear();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var root = drive.RootDirectory.FullName;
            DriveCombo.Items.Add(new DriveItem(root));
        }
    }

    private void DriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DriveCombo.SelectedItem is DriveItem item)
        {
            PathBox.Text = item.Path;
        }
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            PathBox.Text = folder.Path;
        }
    }

    private async void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            PathBox.Text = file.Path;
        }
    }

    private async void FindButton_Click(object sender, RoutedEventArgs e)
        => await RunSearchAsync(fromWatch: false);

    private async Task RunSearchAsync(bool fromWatch)
    {
        var path = PathBox.Text?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            StatusText.Text = "Please enter a drive, file, or folder path.";
            StopWatch(updateToggle: true);
            return;
        }

        // Normalize drive-only input (e.g. "C:" -> "C:\")
        if (path is [_, ':'] && char.IsLetter(path[0]))
        {
            path = path + "\\";
        }

        if (!IsValidPath(path))
        {
            StatusText.Text = $"Path not found: {path}";
            StopWatch(updateToggle: true);
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        if (!fromWatch)
        {
            FindButton.IsEnabled = false;
            ProgressBar.Visibility = Visibility.Visible;
            StatusText.Text = "Searching for handles...";
        }

        try
        {
            var results = await _handleService.FindHandlesForPathAsync(path!, false, _cts.Token);
            _allHandles = results.ToList();
            ApplyFilters();

            string filterNote = _handles.Count != _allHandles.Count
                ? $" (showing {_handles.Count} after filters)"
                : string.Empty;

            StatusText.Text = _allHandles.Count == 0
                ? (fromWatch ? "Watch: path is clear — no locking handles." : "No processes found with handles to this path.")
                : $"Found {_allHandles.Count} handle(s) from {_allHandles.Select(h => h.ProcessId).Distinct().Count()} process(es).{filterNote}";

            if (fromWatch && _allHandles.Count == 0)
            {
                StopWatch(updateToggle: true);
            }
        }
        catch (OperationCanceledException)
        {
            if (!fromWatch)
            {
                StatusText.Text = "Search cancelled.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            if (fromWatch)
            {
                StopWatch(updateToggle: true);
            }
        }
        finally
        {
            if (!fromWatch)
            {
                FindButton.IsEnabled = true;
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void Filter_Changed(object sender, object e) => ApplyFilters();

    private void ApplyFilters()
    {
        string proc = ProcessFilterBox?.Text?.Trim() ?? string.Empty;
        string pidText = PidFilterBox?.Text?.Trim() ?? string.Empty;
        int? pidFilter = int.TryParse(pidText, out int pid) ? pid : null;
        string typeTag = (TypeFilterCombo?.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";

        _handles.Clear();
        foreach (var h in _allHandles)
        {
            if (!string.IsNullOrEmpty(proc) &&
                h.ProcessName.IndexOf(proc, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (pidFilter is int p && h.ProcessId != p)
            {
                continue;
            }

            if (typeTag == "file" && h.ObjectTypeIndex != 28)
            {
                continue;
            }

            if (typeTag == "other" && h.ObjectTypeIndex == 28)
            {
                continue;
            }

            _handles.Add(h);
        }
    }

    private async void WatchToggle_Click(object sender, RoutedEventArgs e)
    {
        if (WatchToggle.IsChecked != true)
        {
            StopWatch(updateToggle: false);
            StatusText.Text = "Watch stopped.";
            return;
        }

        _watchCts?.Cancel();
        _watchCts = new CancellationTokenSource();
        var token = _watchCts.Token;
        ProgressBar.Visibility = Visibility.Visible;
        StatusText.Text = "Watching… re-scanning until no handles remain.";

        try
        {
            while (!token.IsCancellationRequested)
            {
                await RunSearchAsync(fromWatch: true).ConfigureAwait(true);
                if (token.IsCancellationRequested || _allHandles.Count == 0)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // stopped
        }
        finally
        {
            if (WatchToggle.IsChecked != true || _allHandles.Count == 0)
            {
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void StopWatch(bool updateToggle)
    {
        _watchCts?.Cancel();
        _watchCts = null;
        if (updateToggle && WatchToggle != null)
        {
            WatchToggle.IsChecked = false;
        }

        ProgressBar.Visibility = Visibility.Collapsed;
    }

    private static bool IsValidPath(string path)
    {
        try
        {
            if (Path.Exists(path))
            {
                return true;
            }

            // Drive root (e.g. C:\) - Directory.Exists is more reliable for drives
            if (path is [_, ':', ..] && path.EndsWith("\\"))
            {
                return Directory.Exists(path);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e) => FindButton_Click(sender, e);

    private void HandlesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = HandlesList.SelectedItems.Count > 0;
        CloseHandleButton.IsEnabled = hasSelection;
        EndProcessButton.IsEnabled = HandlesList.SelectedItems.Count == 1;
    }

    private async void CloseHandleButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = HandlesList.SelectedItems.OfType<HandleInfo>().ToList();
        if (selected.Count == 0)
        {
            return;
        }

        int closed = 0;
        int failed = 0;
        foreach (var handle in selected)
        {
            if (!await DestructiveConfirm.ConfirmAsync(
                    Content.XamlRoot,
                    "Close handle?",
                    DestructiveConfirm.CloseHandleMessage(handle.ProcessName, handle.ProcessId) +
                    (selected.Count > 1 ? $"\n\n({closed + failed + 1} of {selected.Count})" : string.Empty),
                    "Close handle"))
            {
                StatusText.Text = closed > 0
                    ? $"Closed {closed} handle(s); remaining confirms cancelled."
                    : "Close cancelled.";
                break;
            }

            if (HandleService.TryCloseHandle(handle.ProcessId, handle.HandleValue))
            {
                _allHandles.Remove(handle);
                _handles.Remove(handle);
                closed++;
            }
            else
            {
                failed++;
            }
        }

        if (closed > 0 || failed > 0)
        {
            StatusText.Text = failed == 0
                ? $"Closed {closed} handle(s). Processes may need a restart if they were using the file."
                : $"Closed {closed}; failed {failed}. Protected processes may block close — try ending the process.";
        }
    }

    private async void EndProcessButton_Click(object sender, RoutedEventArgs e)
    {
        if (HandlesList.SelectedItems.OfType<HandleInfo>().FirstOrDefault() is not HandleInfo handle)
        {
            return;
        }

        if (!await DestructiveConfirm.ConfirmWithAcknowledgementAsync(
                Content.XamlRoot,
                "End process?",
                DestructiveConfirm.EndProcessMessage(handle.ProcessName, handle.ProcessId),
                "End process"))
        {
            return;
        }

        if (HandleService.TryTerminateProcess(handle.ProcessId))
        {
            _allHandles.RemoveAll(h => h.ProcessId == handle.ProcessId);
            ApplyFilters();
            StatusText.Text = $"Process {handle.ProcessName} (PID {handle.ProcessId}) ended.";
        }
        else
        {
            StatusText.Text = $"Failed to end process {handle.ProcessName}. It may be protected or already closed.";
        }
    }
}
