namespace DiskHealth;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<RecoverableFileInfo> _files = [];
    private string _selectedDrive = string.Empty;
    private string _destinationFolder = string.Empty;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _recoverCts;

    public MainWindow()
    {
        InitializeComponent();
        TrySetMicaBackdrop();
        TrySetExtendedTitleBar();
        LoadDrives();
    }

    /// <summary>Applies Mica backdrop on Windows 11; no-op on older Windows.</summary>
    private void TrySetMicaBackdrop()
    {
        try
        {
            if (MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
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

    private void LoadDrives()
    {
        var drives = RecoveryService.GetScannableDrives();
        DriveCombo.Items.Clear();
        foreach (var d in drives)
        {
            DriveCombo.Items.Add(d);
        }

        if (DriveCombo.Items.Count > 0)
        {
            DriveCombo.SelectedIndex = 0;
        }
    }

    private void DriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DriveCombo.SelectedItem is ScannableDrive info)
        {
            _selectedDrive = info.Letter;
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedDrive))
        {
            await ShowMessageAsync("Select a drive first.");
            return;
        }

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        ScanButton.IsEnabled = false;
        ProgressBar.Visibility = Visibility.Visible;
        ProgressBar.IsIndeterminate = true;
        StatusText.Visibility = Visibility.Visible;
        StatusText.Text = "Scanning drive for recoverable files...";
        _files.Clear();
        FileListView.ItemsSource = null;

        var progress = new Progress<(long BytesScanned, int FileCount)>(p =>
        {
            StatusText.Text = $"Scanned {p.BytesScanned / (1024 * 1024)} MB, found {p.FileCount} files...";
        });

        try
        {
            var results = await RecoveryService.ScanForRecoverableFilesAsync(_selectedDrive, progress, ct);
            foreach (var f in results)
            {
                _files.Add(f);
            }

            FileListView.ItemsSource = _files;
            FileCountText.Text = $"{_files.Count} files found";
            SelectAllCheck.IsChecked = true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Scan cancelled.";
        }
        catch (UnauthorizedAccessException)
        {
            await ShowMessageAsync("Administrator privileges are required to scan drives. Please run the application as administrator.");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Scan failed: {ex.Message}");
        }
        finally
        {
            ScanButton.IsEnabled = true;
            ProgressBar.Visibility = Visibility.Collapsed;
            if (_files.Count == 0)
            {
                StatusText.Text = "Scan complete. No recoverable files found.";
            }
            else
            {
                StatusText.Text = $"Scan complete. Found {_files.Count} potentially recoverable files.";
            }
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var task = picker.PickSingleFolderAsync().AsTask();
        var folder = task.GetAwaiter().GetResult();
        if (folder != null)
        {
            _destinationFolder = folder.Path;
            DestinationBox.Text = _destinationFolder;
        }
    }

    private void SelectAllCheck_Changed(object sender, RoutedEventArgs e)
    {
        bool check = SelectAllCheck.IsChecked == true;
        foreach (var f in _files)
        {
            f.IsSelected = check;
        }
    }

    private void FileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Optional: sync selection state from ListView if needed
    }

    private async void RecoverButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedDrive))
        {
            await ShowMessageAsync("Select a drive first.");
            return;
        }

        if (string.IsNullOrEmpty(_destinationFolder) || !Directory.Exists(_destinationFolder))
        {
            await ShowMessageAsync("Select a valid recovery destination folder.");
            return;
        }

        var selected = _files.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0)
        {
            await ShowMessageAsync("Select at least one file to recover.");
            return;
        }

        _recoverCts?.Cancel();
        _recoverCts = new CancellationTokenSource();
        var ct = _recoverCts.Token;

        RecoverButton.IsEnabled = false;
        ProgressBar.Visibility = Visibility.Visible;
        ProgressBar.IsIndeterminate = false;
        StatusText.Visibility = Visibility.Visible;

        var progress = new Progress<(int Current, int Total, string? CurrentFile)>(p =>
        {
            ProgressBar.Maximum = p.Total;
            ProgressBar.Value = p.Current;
            StatusText.Text = p.CurrentFile != null ? $"Recovering {p.CurrentFile}..." : $"Recovered {p.Current} of {p.Total}";
        });

        try
        {
            await RecoveryService.RecoverFilesAsync(selected, _selectedDrive, _destinationFolder, progress, ct);
            await ShowMessageAsync($"Successfully recovered {selected.Count} file(s) to {_destinationFolder}");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Recovery cancelled.";
        }
        catch (UnauthorizedAccessException)
        {
            await ShowMessageAsync("Administrator privileges are required. Please run as administrator.");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Recovery failed: {ex.Message}");
        }
        finally
        {
            RecoverButton.IsEnabled = true;
            ProgressBar.Visibility = Visibility.Collapsed;
            StatusText.Text = "Recovery complete.";
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadDrives();
    }

    private void SmartStatsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new SmartStatsWindow();
        window.Activate();
    }

    private async Task ShowMessageAsync(string message)
    {
        try
        {
            if (Content?.XamlRoot == null)
            {
                throw new InvalidOperationException("Window not ready");
            }

            var dialog = new ContentDialog
            {
                Title = "Disk Health",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch
        {
            App.ShowMessageBox("Disk Health", message);
        }
    }
}
