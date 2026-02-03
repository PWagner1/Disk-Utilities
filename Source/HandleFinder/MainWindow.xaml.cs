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
    private CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();
        TrySetMicaBackdrop();
        TrySetExtendedTitleBar();
        TrySetWindowSize(900, 600);
        HandlesList.ItemsSource = _handles;
        LoadDrives();
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

    private void TrySetMicaBackdrop()
    {
        try
        {
            if (MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
            }
        }
        catch { /* Mica not supported */ }
    }

    private void TrySetExtendedTitleBar()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
        }
        catch { /* Fallback to default */ }
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
        catch { /* Ignore */ }
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
    {
        var path = PathBox.Text?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            StatusText.Text = "Please enter a drive, file, or folder path.";
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
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _handles.Clear();
        FindButton.IsEnabled = false;
        ProgressBar.Visibility = Visibility.Visible;
        StatusText.Text = "Searching for handles...";

        try
        {
            var results = await _handleService.FindHandlesForPathAsync(path!, false, _cts.Token);

            foreach (var h in results)
            {
                _handles.Add(h);
            }

            StatusText.Text = results.Count == 0
                ? "No processes found with handles to this path."
                : $"Found {results.Count} handle(s) from {results.Select(h => h.ProcessId).Distinct().Count()} process(es).";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Search cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            FindButton.IsEnabled = true;
            ProgressBar.Visibility = Visibility.Collapsed;
        }
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
        var hasSelection = HandlesList.SelectedItem != null;
        CloseHandleButton.IsEnabled = hasSelection;
        EndProcessButton.IsEnabled = hasSelection;
    }

    private async void CloseHandleButton_Click(object sender, RoutedEventArgs e)
    {
        if (HandlesList.SelectedItem is not HandleInfo handle)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Close handle?",
            Content = $"Close the handle in '{handle.ProcessName}' (PID {handle.ProcessId})? The process will continue running; closing the handle may allow the file to be deleted or moved. The process could crash if it is actively using the file.",
            PrimaryButtonText = "Close handle",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (HandleService.TryCloseHandle(handle.ProcessId, handle.HandleValue))
        {
            _handles.Remove(handle);
            StatusText.Text = $"Handle closed. {handle.ProcessName} may need to be restarted if it was using the file.";
        }
        else
        {
            StatusText.Text = $"Could not close handle. The process may be protected, or this Windows version may not support it. Try ending the process instead.";
        }
    }

    private async void EndProcessButton_Click(object sender, RoutedEventArgs e)
    {
        if (HandlesList.SelectedItem is not HandleInfo handle)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "End process?",
            Content = $"End process '{handle.ProcessName}' (PID {handle.ProcessId})? This may cause data loss or instability.",
            PrimaryButtonText = "End process",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (HandleService.TryTerminateProcess(handle.ProcessId))
        {
            _handles.Remove(handle);
            StatusText.Text = $"Process {handle.ProcessName} (PID {handle.ProcessId}) ended.";
        }
        else
        {
            StatusText.Text = $"Failed to end process {handle.ProcessName}. It may be protected or already closed.";
        }
    }
}
