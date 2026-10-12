using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace DiskHealth;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<RecoverableFileInfo> _files = [];
    private string _selectedDrive = string.Empty;
    private string _destinationFolder = string.Empty;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _recoverCts;
    private ScanPauseGate? _pauseGate;
    private bool _isPaused;
    private string _listTypeFilter = "";

    public MainWindow()
    {
        InitializeComponent();
        WindowChrome.TryApplyMica(this);
        WindowChrome.TryExtendTitleBar(this, AppTitleBar);
        ConfigureElevationBanner();
        PopulateTypeFilter();
        LoadDrives();
    }

    private void PopulateTypeFilter()
    {
        foreach (string ext in RecoveryService.KnownExtensions)
        {
            TypeFilterCombo.Items.Add(new ComboBoxItem { Content = ext, Tag = ext });
        }
    }

    private void ConfigureElevationBanner()
    {
        ElevationUi.StyleWarningBanner(ElevationBanner);
        ElevationUi.ConfigureBanner(
            ElevationBanner,
            ElevationBannerText,
            "Not running as Administrator. Signature carve / MFT undelete need raw disk access.",
            RestartAsAdminButton,
            null,
            "Carving and MFT scans need Administrator rights for raw volume access.");
        UpdateModeUi();
    }

    private string CurrentRecoveryMode =>
        RecoveryModeCombo.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : "carve";

    private void RecoveryModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateModeUi();

    private void UpdateModeUi()
    {
        if (ModeHelpText == null || ScanModeCombo == null || ScanButton == null || PauseButton == null)
        {
            return;
        }

        string mode = CurrentRecoveryMode;
        ScanModeCombo.Visibility = mode == "carve" ? Visibility.Visible : Visibility.Collapsed;
        PauseButton.Visibility = mode == "carve" ? Visibility.Visible : Visibility.Collapsed;
        TypeFilterCombo.Visibility = mode == "carve" ? Visibility.Visible : Visibility.Collapsed;

        ModeHelpText.Text = mode switch
        {
            "recycle" => "Recycle Bin mode lists $I/$R entries on the selected volume and restores the $R payload. This is undelete from the Recycle Bin — not signature carving.",
            "mft" => "MFT deleted mode scans NTFS Master File Table records that are no longer in-use. Cluster data may already be reused — chance scores are heuristic. Not a full forensic undelete suite.",
            _ => "Signature carving scans raw drive data for known file headers. This is not NTFS MFT or Recycle Bin undelete. Administrator privileges are required."
        };

        CandidatesHeader.Text = mode switch
        {
            "recycle" => "Recycle Bin items",
            "mft" => "MFT deleted candidates",
            _ => "Carved candidates"
        };

        ScanButton.Content = mode switch
        {
            "recycle" => "List Recycle Bin",
            "mft" => "Scan MFT",
            _ => "Carve"
        };
    }

    private void LoadDrives()
    {
        AppLog.ClearLastError();
        var drives = RecoveryService.GetScannableDrives();
        DriveCombo.Items.Clear();
        foreach (var d in drives)
        {
            DriveCombo.Items.Add(d);
        }

        if (DriveCombo.Items.Count > 0)
        {
            DriveCombo.SelectedIndex = 0;
            return;
        }

        string? error = AppLog.ConsumeLastError();
        StatusText.Visibility = Visibility.Visible;
        StatusText.Text = !string.IsNullOrEmpty(error)
            ? error
            : ElevationHelper.IsElevated
                ? "No drives found."
                : "No drives found. Restart as Administrator if drive enumeration failed.";
    }

    private void DriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DriveCombo.SelectedItem is ScannableDrive info)
        {
            _selectedDrive = info.Letter;
        }
    }

    private void TypeFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TypeFilterCombo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            _listTypeFilter = tag ?? "";
            ApplyListFilter();
        }
    }

    private void ApplyListFilter()
    {
        if (string.IsNullOrEmpty(_listTypeFilter))
        {
            FileListView.ItemsSource = _files;
            FileCountText.Text = $"{_files.Count} candidates found";
            return;
        }

        var filtered = _files
            .Where(f => f.FileType.Equals(_listTypeFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        FileListView.ItemsSource = filtered;
        FileCountText.Text = $"{filtered.Count} of {_files.Count} candidates (filtered)";
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedDrive))
        {
            await ShowMessageAsync("Select a drive first.");
            return;
        }

        string mode = CurrentRecoveryMode;
        bool needsAdmin = mode is "carve" or "mft";
        if (needsAdmin && !ElevationHelper.IsElevated && Content?.XamlRoot is { } elevRoot)
        {
            bool abort = await ElevationUi.EnsureElevatedOrAbortAsync(
                elevRoot,
                mode == "mft"
                    ? "MFT undelete needs Administrator rights for raw volume access."
                    : "Signature carving needs Administrator rights for raw volume access.",
                requireElevation: true).ConfigureAwait(true);
            if (abort)
            {
                return;
            }
        }

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;
        _pauseGate = new ScanPauseGate();
        _isPaused = false;
        PauseButton.Content = "Pause";

        SetScanningUi(true);
        ProgressBar.Visibility = Visibility.Visible;
        ProgressBar.IsIndeterminate = true;
        StatusText.Visibility = Visibility.Visible;
        _files.Clear();
        FileListView.ItemsSource = null;
        ClearPreview();

        try
        {
            IReadOnlyList<RecoverableFileInfo> results = mode switch
            {
                "recycle" => await ScanRecycleAsync(ct),
                "mft" => await ScanMftAsync(ct),
                _ => await ScanCarveAsync(ct)
            };

            foreach (var f in results)
            {
                _files.Add(f);
            }

            ApplyListFilter();
            SelectAllCheck.IsChecked = true;
            ExportButton.IsEnabled = _files.Count > 0;
            StatusText.Text = _files.Count == 0
                ? $"{ModeCompleteLabel(mode)} No candidates found."
                : $"{ModeCompleteLabel(mode)} Found {_files.Count} candidate(s). Source column shows Carve vs Recycle Bin vs MFT.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Scan cancelled.";
        }
        catch (UnauthorizedAccessException)
        {
            if (Content?.XamlRoot is { } root && !ElevationHelper.IsElevated)
            {
                await ElevationUi.PromptAndRelaunchAsync(
                    root,
                    "Administrator privileges are required to scan drives.").ConfigureAwait(true);
            }
            else
            {
                await ShowMessageAsync("Administrator privileges are required to scan drives. Please run the application as administrator.");
            }
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Scan failed: {ex.Message}");
        }
        finally
        {
            SetScanningUi(false);
            ProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private static string ModeCompleteLabel(string mode) => mode switch
    {
        "recycle" => "Recycle Bin scan complete.",
        "mft" => "MFT deleted scan complete.",
        _ => "Carve complete."
    };

    private async Task<IReadOnlyList<RecoverableFileInfo>> ScanCarveAsync(CancellationToken ct)
    {
        bool quick = ScanModeCombo.SelectedItem is ComboBoxItem { Tag: "quick" }
                     || ScanModeCombo.SelectedIndex == 0;
        var options = new ScanOptions
        {
            QuickScan = quick,
            PauseGate = _pauseGate
        };
        StatusText.Text = quick ? "Quick carve (first 8 GB)..." : "Deep carve (full volume)...";
        var progress = new Progress<(long BytesScanned, int FileCount)>(p =>
        {
            StatusText.Text = $"{(_isPaused ? "Paused — " : "")}Carved {p.BytesScanned / (1024 * 1024)} MB, found {p.FileCount} candidates...";
        });
        return await RecoveryService.ScanForRecoverableFilesAsync(_selectedDrive, options, progress, ct);
    }

    private async Task<IReadOnlyList<RecoverableFileInfo>> ScanRecycleAsync(CancellationToken ct)
    {
        StatusText.Text = "Listing Recycle Bin ($Recycle.Bin)...";
        return await RecycleBinRecovery.ScanAsync(_selectedDrive, ct);
    }

    private async Task<IReadOnlyList<RecoverableFileInfo>> ScanMftAsync(CancellationToken ct)
    {
        StatusText.Text = "Scanning NTFS MFT for deleted FILE records...";
        var progress = new Progress<(long Records, int Found)>(p =>
        {
            StatusText.Text = $"MFT records scanned: {p.Records:N0}; deleted candidates: {p.Found}...";
        });
        return await MftDeletedScanner.ScanAsync(_selectedDrive, progress, ct);
    }

    private void SetScanningUi(bool scanning)
    {
        ScanButton.IsEnabled = !scanning;
        PauseButton.IsEnabled = scanning && CurrentRecoveryMode == "carve";
        CancelScanButton.IsEnabled = scanning;
        ScanModeCombo.IsEnabled = !scanning;
        DriveCombo.IsEnabled = !scanning;
        RecoveryModeCombo.IsEnabled = !scanning;
        ImageVolumeButton.IsEnabled = !scanning;
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pauseGate == null)
        {
            return;
        }

        if (_isPaused)
        {
            _pauseGate.Resume();
            _isPaused = false;
            PauseButton.Content = "Pause";
            StatusText.Text = "Resuming carve...";
        }
        else
        {
            _pauseGate.Pause();
            _isPaused = true;
            PauseButton.Content = "Resume";
            StatusText.Text = "Paused. Click Resume to continue.";
        }
    }

    private void CancelScanButton_Click(object sender, RoutedEventArgs e)
    {
        _pauseGate?.Resume();
        _scanCts?.Cancel();
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

    private async void FileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileListView.SelectedItem is RecoverableFileInfo file)
        {
            await UpdatePreviewAsync(file);
        }
    }

    private async Task UpdatePreviewAsync(RecoverableFileInfo file)
    {
        PreviewMetaText.Text = $"{file.SourceLabel} · {file.FileType} · {file.RecoveryChanceLabel}"
            + (file.SourceKind == RecoverySourceKind.Carve ? $" · offset {file.Offset:N0}" : string.Empty)
            + (!string.IsNullOrEmpty(file.PathHint) ? $" · {file.PathHint}" : string.Empty);
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
        PreviewText.Text = "Loading preview...";

        if (file.SourceKind == RecoverySourceKind.RecycleBin)
        {
            PreviewText.Text = string.IsNullOrEmpty(file.PathHint)
                ? "Recycle Bin item (no $R path)."
                : $"Recycle Bin $R payload:\n{file.PathHint}\nSize ≈ {ByteFormatter.Format((ulong)Math.Max(0, file.EstimatedSize))}";
            return;
        }

        if (file.SourceKind == RecoverySourceKind.MftDeleted)
        {
            PreviewText.Text = file.DataRuns is { Count: > 0 }
                ? $"MFT deleted file with {file.DataRuns.Count} data run(s). Clusters may be reused — recover to a quarantine folder."
                : "MFT deleted filename found, but no usable non-resident data runs were parsed. Recovery may fail.";
            return;
        }

        if (string.IsNullOrEmpty(_selectedDrive))
        {
            PreviewText.Text = "No drive selected.";
            return;
        }

        try
        {
            string ext = file.FileType.ToLowerInvariant();
            bool isImage = ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp";
            int bytes = isImage ? 2 * 1024 * 1024 : 4096;
            byte[] data = await RecoveryService.ReadPreviewAsync(_selectedDrive, file.Offset, bytes);

            if (data.Length == 0)
            {
                PreviewText.Text = "Could not read preview data.";
                return;
            }

            if (isImage)
            {
                try
                {
                    using var stream = new InMemoryRandomAccessStream();
                    using (var writer = new DataWriter(stream))
                    {
                        writer.WriteBytes(data);
                        await writer.StoreAsync();
                        await writer.FlushAsync();
                        writer.DetachStream();
                    }

                    stream.Seek(0);
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream);
                    PreviewImage.Source = bitmap;
                    PreviewImage.Visibility = Visibility.Visible;
                    PreviewText.Text = "Image preview (carved slice; may be incomplete).";
                    return;
                }
                catch
                {
                    PreviewText.Text = "Image header found, but preview decode failed (incomplete carve).";
                    return;
                }
            }

            if (ext == ".pdf")
            {
                string header = Encoding.ASCII.GetString(data.AsSpan(0, Math.Min(data.Length, 120)));
                PreviewText.Text = $"PDF header:\n{header}";
                return;
            }

            PreviewText.Text = $"Binary preview ({data.Length} bytes). Recover to inspect the full carve.";
        }
        catch (Exception ex)
        {
            PreviewText.Text = $"Preview unavailable: {ex.Message}";
        }
    }

    private void ClearPreview()
    {
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewText.Text = "Select a candidate to preview (images and PDF headers).";
        PreviewMetaText.Text = "";
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_files.Count == 0)
        {
            await ShowMessageAsync("Nothing to export. Run a carve first.");
            return;
        }

        var picker = new Windows.Storage.Pickers.FileSavePicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedFileName = "DiskHealth-candidates";
        picker.FileTypeChoices.Add("CSV", [".csv"]);
        picker.FileTypeChoices.Add("JSON", [".json"]);

        var file = await picker.PickSaveFileAsync();
        if (file == null)
        {
            return;
        }

        try
        {
            string content = file.FileType.Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? RecoveryExport.ToJson(_files)
                : RecoveryExport.ToCsv(_files);
            await Windows.Storage.FileIO.WriteTextAsync(file, content);
            StatusText.Visibility = Visibility.Visible;
            StatusText.Text = $"Exported {_files.Count} candidates to {file.Path}";
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Export failed: {ex.Message}");
        }
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

        bool needsAdmin = selected.Any(f => f.SourceKind is RecoverySourceKind.Carve or RecoverySourceKind.MftDeleted);
        if (needsAdmin && !ElevationHelper.IsElevated && Content?.XamlRoot is { } elevRoot)
        {
            bool abort = await ElevationUi.EnsureElevatedOrAbortAsync(
                elevRoot,
                "Recovering carved / MFT files needs Administrator rights for raw volume access.",
                requireElevation: true).ConfigureAwait(true);
            if (abort)
            {
                return;
            }
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
            ProgressBar.Maximum = Math.Max(1, p.Total);
            ProgressBar.Value = p.Current;
            StatusText.Text = p.CurrentFile != null ? $"Recovering {p.CurrentFile}..." : $"Recovered {p.Current} of {p.Total}";
        });

        try
        {
            var carve = selected.Where(f => f.SourceKind == RecoverySourceKind.Carve).ToList();
            var undelete = selected.Where(f => f.SourceKind != RecoverySourceKind.Carve).ToList();
            if (carve.Count > 0)
            {
                await RecoveryService.RecoverFilesAsync(carve, _selectedDrive, _destinationFolder, progress, ct);
            }

            if (undelete.Count > 0)
            {
                await UndeleteRecovery.RecoverAsync(undelete, _selectedDrive, _destinationFolder, progress, ct);
            }

            await ShowMessageAsync($"Finished recovery of {selected.Count} selected item(s) to {_destinationFolder}");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Recovery cancelled.";
        }
        catch (UnauthorizedAccessException)
        {
            if (Content?.XamlRoot is { } root && !ElevationHelper.IsElevated)
            {
                await ElevationUi.PromptAndRelaunchAsync(
                    root,
                    "Administrator privileges are required to recover files from raw volumes.").ConfigureAwait(true);
            }
            else
            {
                await ShowMessageAsync("Administrator privileges are required. Please run as administrator.");
            }
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

    private async void ImageVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedDrive))
        {
            await ShowMessageAsync("Select a drive first.");
            return;
        }

        if (!ElevationHelper.IsElevated && Content?.XamlRoot is { } elevRoot)
        {
            bool abort = await ElevationUi.EnsureElevatedOrAbortAsync(
                elevRoot,
                "Imaging a volume needs Administrator rights for raw disk access.",
                requireElevation: true).ConfigureAwait(true);
            if (abort)
            {
                return;
            }
        }

        var picker = new Windows.Storage.Pickers.FileSavePicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedFileName = $"DiskHealth-{_selectedDrive.TrimEnd(':')}-image";
        picker.FileTypeChoices.Add("Raw volume image", [".img", ".dd"]);

        var file = await picker.PickSaveFileAsync();
        if (file == null)
        {
            return;
        }

        if (Content?.XamlRoot is null)
        {
            return;
        }

        var sizeDialog = new ContentDialog
        {
            Title = "Volume image size",
            Content = "Create a read-only raw image for forensic-lite work. Choose how much to copy:",
            PrimaryButtonText = "First 8 GB",
            SecondaryButtonText = "Entire volume",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        var choice = await sizeDialog.ShowAsync();
        if (choice == ContentDialogResult.None)
        {
            return;
        }

        long maxBytes = choice == ContentDialogResult.Primary ? 8L * 1024 * 1024 * 1024 : 0;
        SetScanningUi(true);
        ProgressBar.Visibility = Visibility.Visible;
        ProgressBar.IsIndeterminate = false;
        ProgressBar.Maximum = 100;
        ProgressBar.Value = 0;
        StatusText.Visibility = Visibility.Visible;
        StatusText.Text = "Creating volume image...";
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<(long Written, long Total)>(p =>
            {
                if (p.Total > 0)
                {
                    ProgressBar.Value = Math.Min(100, p.Written * 100.0 / p.Total);
                }

                StatusText.Text = $"Imaging… {ByteFormatter.Format((ulong)p.Written)} / {ByteFormatter.Format((ulong)Math.Max(0, p.Total))}";
            });
            await VolumeImageService.CreateImageAsync(_selectedDrive, file.Path, maxBytes, progress, _scanCts.Token);
            await ShowMessageAsync($"Volume image written to:\n{file.Path}");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Image cancelled.";
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Image failed: {ex.Message}");
        }
        finally
        {
            SetScanningUi(false);
            ProgressBar.Visibility = Visibility.Collapsed;
        }
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
