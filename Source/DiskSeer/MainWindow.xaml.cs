namespace DiskSeer;

/// <summary>
/// Main window for browsing and modifying Apple and Linux file systems.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly ForeignDriveService _driveService = new();
    private IReadOnlyList<ForeignDriveInfo> _drives = [];
    private IReadOnlyList<FileSystemEntry> _currentEntries = [];
    private IReadOnlyList<FileSystemEntry> _filteredEntries = [];

    private string _currentPath = "/";
    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();

    private ForeignDriveInfo? _selectedDrive;
    private ForeignPartitionInfo? _selectedPartition;

    private CancellationTokenSource? _operationCts;

    public MainWindow()
    {
        InitializeComponent();

        // Set up title bar
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Try to apply Mica backdrop
        TrySetMicaBackdrop();

        // Set window size
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(1400, 900));

        ConfigureElevationBanner();

        // Initial drive refresh
        RefreshDrives();
    }

    private void ConfigureElevationBanner()
    {
        ElevationUi.StyleWarningBanner(ElevationBanner);
        ElevationUi.ConfigureBanner(
            ElevationBanner,
            ElevationBannerText,
            "Not running as Administrator. DiskSeer needs elevation for raw disk access to foreign file systems.",
            RestartAsAdminButton,
            null,
            "Browsing Apple and Linux volumes needs Administrator rights for raw disk access.");
    }

    private void TrySetMicaBackdrop()
    {
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
        }
    }

    #region Drive Selection

    private async void RefreshDrives()
    {
        StatusText.Text = "Scanning for drives (multicore)...";
        RefreshDrivesButton.IsEnabled = false;
        OperationProgress.IsIndeterminate = true;
        OperationProgress.Visibility = Visibility.Visible;

        try
        {
            // Use parallel drive detection on the thread pool to keep UI responsive
            var drives = await ForeignDriveService.GetAllPhysicalDrivesAsync();

            // Fallback: if parallel path returned empty, try synchronous (WMI/threading edge cases)
            if (drives.Count == 0)
            {
                drives = await ForeignDriveService.GetAllPhysicalDrivesAsync();
            }

            var drivesToShow = drives;

            // Marshal UI updates to the UI thread (WinUI 3 may not have SyncContext)
            DispatcherQueue.TryEnqueue(() =>
            {
                _drives = drivesToShow;
                DriveCombo.Items.Clear();

                foreach (var drive in _drives)
                {
                    int foreignCount = drive.Partitions.Count(p => p.FileSystemType != ForeignFileSystemType.Unknown);
                    string suffix = foreignCount > 0 ? $" ({foreignCount} foreign partition{(foreignCount > 1 ? "s" : "")})" : " (no foreign partitions)";

                    DriveCombo.Items.Add(new ComboBoxItem
                    {
                        Content = drive.DisplayText + suffix,
                        Tag = drive
                    });
                }

                StatusText.Text = _drives.Count > 0
                    ? $"Found {_drives.Count} physical drive{(_drives.Count != 1 ? "s" : "")}"
                    : ElevationHelper.IsElevated
                        ? "No drives found."
                        : "No drives found. Restart as Administrator — raw disk access requires elevation.";
            });
        }
        catch (OperationCanceledException)
        {
            DispatcherQueue.TryEnqueue(() => StatusText.Text = "Scan cancelled");
        }
        catch (Exception ex)
        {
            DispatcherQueue.TryEnqueue(() => StatusText.Text = $"Error scanning drives: {ex.Message}");
        }
        finally
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                RefreshDrivesButton.IsEnabled = true;
                OperationProgress.Visibility = Visibility.Collapsed;
            });
        }
    }

    private void RefreshDrivesButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshDrives();
    }

    private void DriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PartitionCombo.Items.Clear();
        PartitionCombo.IsEnabled = false;
        OpenPartitionButton.IsEnabled = false;
        _selectedDrive = null;
        _selectedPartition = null;

        if (DriveCombo.SelectedItem is ComboBoxItem { Tag: ForeignDriveInfo drive })
        {
            _selectedDrive = drive;

            foreach (var partition in drive.Partitions)
            {
                string status = partition.IsSupported ? "" : " [Not supported]";
                PartitionCombo.Items.Add(new ComboBoxItem
                {
                    Content = partition.DisplayText + status,
                    Tag = partition,
                    IsEnabled = partition.IsSupported
                });
            }

            if (drive.Partitions.Count > 0)
            {
                PartitionCombo.IsEnabled = true;
                // Auto-select first supported partition
                var firstSupported = drive.Partitions.FindIndex(p => p.IsSupported);
                if (firstSupported >= 0)
                {
                    PartitionCombo.SelectedIndex = firstSupported;
                }
            }
            else
            {
                StatusText.Text = "No partitions found on selected drive";
            }
        }
    }

    private void PartitionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedPartition = null;
        OpenPartitionButton.IsEnabled = false;

        if (PartitionCombo.SelectedItem is ComboBoxItem { Tag: ForeignPartitionInfo partition })
        {
            _selectedPartition = partition;
            OpenPartitionButton.IsEnabled = partition.IsSupported;

            if (!partition.IsSupported)
            {
                StatusText.Text = $"{partition.FileSystemType} is not currently supported for browsing";
            }
            else
            {
                StatusText.Text = $"Selected {partition.FileSystemType} partition - Click 'Open' to browse";
            }
        }
    }

    private async void OpenPartitionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDrive == null || _selectedPartition == null)
        {
            return;
        }

        ShowLoading("Opening partition...");

        try
        {
            bool success = await Task.Run(() =>
                _driveService.OpenPartition(_selectedDrive.DriveIndex, _selectedPartition.PartitionIndex));

            if (success)
            {
                _currentPath = "/";
                _backHistory.Clear();
                _forwardHistory.Clear();
                UpdateNavigationButtons();

                // Update volume info
                VolumeLabelText.Text = $"Label: {_driveService.GetVolumeLabel()}";
                VolumeTypeText.Text = $"Type: {_selectedPartition.FileSystemType}";
                VolumeSizeText.Text = $"Size: {FormatSize(_selectedPartition.Size)}";

                await LoadDirectoryAsync(_currentPath);
                StatusText.Text = $"Opened {_selectedPartition.FileSystemType} partition";
            }
            else
            {
                ShowEmptyState();
                StatusText.Text = "Failed to open partition. Make sure you're running as administrator.";
            }
        }
        catch (Exception ex)
        {
            ShowEmptyState();
            StatusText.Text = $"Error opening partition: {ex.Message}";
        }
    }

    #endregion

    #region Navigation

    private async Task LoadDirectoryAsync(string path)
    {
        ShowLoading($"Loading {path}...");

        try
        {
            _currentEntries = await Task.Run(() => _driveService.GetDirectoryContents(path));
            _filteredEntries = _currentEntries;
            _currentPath = path;

            UpdateBreadcrumbs();
            UpdateNavigationButtons();
            ApplyFilter();

            FileListView.ItemsSource = _filteredEntries;

            HideLoading();
            FileListView.Visibility = Visibility.Visible;

            StatusText.Text = $"{_currentEntries.Count} item{(_currentEntries.Count != 1 ? "s" : "")}";

            // Reset selection UI
            ShowVolumeInfo();
        }
        catch (Exception ex)
        {
            ShowEmptyState();
            StatusText.Text = $"Error loading directory: {ex.Message}";
        }
    }

    private void UpdateBreadcrumbs()
    {
        var items = new List<BreadcrumbItem> { new("Root", "/") };

        if (_currentPath != "/")
        {
            var parts = _currentPath.Trim('/').Split('/');
            var currentBuildPath = "";

            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part))
                {
                    continue;
                }

                currentBuildPath += "/" + part;
                items.Add(new BreadcrumbItem(part, currentBuildPath));
            }
        }

        BreadcrumbRepeater.ItemsSource = items;
    }

    private void UpdateNavigationButtons()
    {
        BackButton.IsEnabled = _backHistory.Count > 0;
        ForwardButton.IsEnabled = _forwardHistory.Count > 0;
        UpButton.IsEnabled = _currentPath != "/";
    }

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_backHistory.Count > 0)
        {
            _forwardHistory.Push(_currentPath);
            var previousPath = _backHistory.Pop();
            await LoadDirectoryAsync(previousPath);
        }
    }

    private async void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_forwardHistory.Count > 0)
        {
            _backHistory.Push(_currentPath);
            var nextPath = _forwardHistory.Pop();
            await LoadDirectoryAsync(nextPath);
        }
    }

    private async void UpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath == "/")
        {
            return;
        }

        var parentPath = Path.GetDirectoryName(_currentPath.Replace('/', Path.DirectorySeparatorChar))
            ?.Replace(Path.DirectorySeparatorChar, '/') ?? "/";

        if (string.IsNullOrEmpty(parentPath) || !parentPath.StartsWith('/'))
        {
            parentPath = "/";
        }

        _backHistory.Push(_currentPath);
        _forwardHistory.Clear();
        await LoadDirectoryAsync(parentPath);
    }

    private async void BreadcrumbItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
        {
            if (path != _currentPath)
            {
                _backHistory.Push(_currentPath);
                _forwardHistory.Clear();
                await LoadDirectoryAsync(path);
            }
        }
    }

    private async void NavigateToPath(string path)
    {
        _backHistory.Push(_currentPath);
        _forwardHistory.Clear();
        await LoadDirectoryAsync(path);
    }

    #endregion

    #region File List Interaction

    private async void FileListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (FileListView.SelectedItem is FileSystemEntry entry)
        {
            if (entry.IsDirectory)
            {
                NavigateToPath(entry.FullPath);
            }
            else
            {
                // Open file - copy to temp and open with default app
                await OpenFileAsync(entry);
            }
        }
    }

    private void FileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedItems = FileListView.SelectedItems.Cast<FileSystemEntry>().ToList();

        if (selectedItems.Count == 0)
        {
            ShowVolumeInfo();
        }
        else if (selectedItems.Count == 1)
        {
            ShowFileInfo(selectedItems[0]);
        }
        else
        {
            ShowMultiSelectInfo(selectedItems);
        }
    }

    private void FileListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        // Context menu handled by XAML ContextFlyout
    }

    private void ShowVolumeInfo()
    {
        VolumeInfoPanel.Visibility = Visibility.Visible;
        FileInfoPanel.Visibility = Visibility.Collapsed;
        MultiSelectPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowFileInfo(FileSystemEntry entry)
    {
        VolumeInfoPanel.Visibility = Visibility.Collapsed;
        FileInfoPanel.Visibility = Visibility.Visible;
        MultiSelectPanel.Visibility = Visibility.Collapsed;

        FileInfoIcon.Glyph = entry.IconGlyph;
        FileNameText.Text = entry.Name;
        FileTypeText.Text = entry.IsDirectory ? "Type: Folder" : $"Type: {Path.GetExtension(entry.Name)} file";
        FileSizeText.Text = entry.IsDirectory ? "Size: -" : $"Size: {FormatSize(entry.Size)}";
        FileModifiedText.Text = $"Modified: {entry.LastModified:yyyy-MM-dd HH:mm:ss}";
        FileCreatedText.Text = entry.CreationTime.HasValue
            ? $"Created: {entry.CreationTime:yyyy-MM-dd HH:mm:ss}"
            : "Created: -";
        FilePermissionsText.Text = $"Permissions: {entry.Permissions}";
        FilePathText.Text = $"Path: {entry.FullPath}";
    }

    private void ShowMultiSelectInfo(List<FileSystemEntry> entries)
    {
        VolumeInfoPanel.Visibility = Visibility.Collapsed;
        FileInfoPanel.Visibility = Visibility.Collapsed;
        MultiSelectPanel.Visibility = Visibility.Visible;

        int files = entries.Count(e => !e.IsDirectory);
        int folders = entries.Count(e => e.IsDirectory);
        long totalSize = entries.Where(e => !e.IsDirectory).Sum(e => e.Size);

        string itemsText = "";
        if (files > 0)
        {
            itemsText += $"{files} file{(files != 1 ? "s" : "")}";
        }

        if (folders > 0)
        {
            if (files > 0)
            {
                itemsText += ", ";
            }

            itemsText += $"{folders} folder{(folders != 1 ? "s" : "")}";
        }

        MultiSelectText.Text = $"{entries.Count} items selected ({itemsText})";
        MultiSelectSizeText.Text = $"Total size: {FormatSize(totalSize)}";
    }

    #endregion

    #region Search/Filter

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var searchText = SearchBox.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(searchText))
        {
            _filteredEntries = _currentEntries;
        }
        else
        {
            _filteredEntries = _currentEntries
                .Where(e => e.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        FileListView.ItemsSource = _filteredEntries;
    }

    #endregion

    #region Context Menu Actions

    private async void OpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is FileSystemEntry entry)
        {
            if (entry.IsDirectory)
            {
                NavigateToPath(entry.FullPath);
            }
            else
            {
                await OpenFileAsync(entry);
            }
        }
    }

    private async Task OpenFileAsync(FileSystemEntry entry)
    {
        try
        {
            // Copy to temp location and open
            var tempPath = Path.Combine(Path.GetTempPath(), "DiskSeer", entry.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

            ShowOperationProgress();
            StatusText.Text = $"Copying {entry.Name} to temp...";

            _operationCts = new CancellationTokenSource();

            await _driveService.CopyFileToLocalAsync(
                entry.FullPath,
                tempPath,
                new Progress<(long copied, long total)>(p =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        OperationProgress.Value = p.total > 0 ? (double)p.copied / p.total * 100 : 0;
                    });
                }),
                _operationCts.Token);

            HideOperationProgress();

            // Open with default app
            var psi = new System.Diagnostics.ProcessStartInfo(tempPath)
            {
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);

            StatusText.Text = $"Opened {entry.Name}";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Operation cancelled";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error opening file: {ex.Message}";
        }
        finally
        {
            HideOperationProgress();
            _operationCts = null;
        }
    }

    private async void CopyToLocalMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = FileListView.SelectedItems.Cast<FileSystemEntry>().ToList();
        if (selectedItems.Count == 0)
        {
            return;
        }

        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.Downloads;
        picker.FileTypeFilter.Add("*");

        var hWnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hWnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder == null)
        {
            return;
        }

        ShowOperationProgress();
        _operationCts = new CancellationTokenSource();
        var token = _operationCts.Token;

        try
        {
            var filesOnly = selectedItems.Where(fileSystemEntry => !fileSystemEntry.IsDirectory).ToList();
            var dirs = selectedItems.Where(entry => entry.IsDirectory).ToList();

            // Multicore: batch copy multiple files concurrently (limited parallelism to avoid overwhelming disk)
            if (filesOnly.Count > 1)
            {
                var pairs = filesOnly.Select(f => (f.FullPath, Path.Combine(folder.Path, f.Name))).ToList();
                await _driveService.CopyFilesToLocalAsync(
                    pairs,
                    maxConcurrency: ForeignDriveService.DefaultCopyParallelism,
                    new Progress<(int completed, int total)>(p =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            StatusText.Text = $"Copying... ({p.completed}/{p.total} files)";
                            OperationProgress.Value = p.total > 0 ? (double)p.completed / p.total * 100 : 0;
                        });
                    }),
                    token);
            }
            else if (filesOnly.Count == 1)
            {
                StatusText.Text = $"Copying {filesOnly[0].Name}...";
                await _driveService.CopyFileToLocalAsync(
                    filesOnly[0].FullPath,
                    Path.Combine(folder.Path, filesOnly[0].Name),
                    new Progress<(long copied, long total)>(p =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            OperationProgress.Value = p.total > 0 ? (double)p.copied / p.total * 100 : 0;
                        });
                    }),
                    token);
            }

            // Directories: copy sequentially (each is recursive)
            int completed = filesOnly.Count;
            foreach (var entry in dirs)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                DispatcherQueue.TryEnqueue(() => StatusText.Text = $"\u0043\u006f\u0070\u0079\u0069\u006e\u0067\u0020{entry.Name}\u002e\u002e\u002e\u0020\u0028{completed + 1}\u002f{selectedItems.Count}\u0029");
                var destPath = Path.Combine(folder.Path, entry.Name);
                await CopyDirectoryToLocalAsync(entry.FullPath, destPath);
                completed++;
            }

            DispatcherQueue.TryEnqueue(() =>
                StatusText.Text = $"Copied {completed} item{(completed != 1 ? "s" : "")} to {folder.Path}");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Copy cancelled";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Copy error: {ex.Message}";
        }
        finally
        {
            HideOperationProgress();
            _operationCts = null;
        }
    }

    private async Task CopyDirectoryToLocalAsync(string sourcePath, string destPath)
    {
        Directory.CreateDirectory(destPath);

        var entries = _driveService.GetDirectoryContents(sourcePath);

        foreach (var entry in entries)
        {
            _operationCts?.Token.ThrowIfCancellationRequested();

            var itemDest = Path.Combine(destPath, entry.Name);

            if (entry.IsDirectory)
            {
                await CopyDirectoryToLocalAsync(entry.FullPath, itemDest);
            }
            else
            {
                await _driveService.CopyFileToLocalAsync(entry.FullPath, itemDest, null, _operationCts?.Token ?? default);
            }
        }
    }

    private async void CopyFromLocalMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.Downloads;
        picker.FileTypeFilter.Add("*");

        var hWnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hWnd);

        var files = await picker.PickMultipleFilesAsync();
        if (files == null || files.Count == 0)
        {
            return;
        }

        ShowOperationProgress();
        _operationCts = new CancellationTokenSource();
        var token = _operationCts.Token;

        try
        {
            var basePath = _currentPath.TrimEnd('/');
            var pairs = files.Select(f => (SourcePath: f.Path, DestPath: basePath + "/" + f.Name)).ToList();

            if (pairs.Count > 1)
            {
                await _driveService.CopyFilesFromLocalAsync(
                    pairs,
                    maxConcurrency: ForeignDriveService.DefaultCopyParallelism,
                    new Progress<(int completed, int total)>(p =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            StatusText.Text = $"Copying... ({p.completed}/{p.total} files)";
                            OperationProgress.Value = p.total > 0 ? (double)p.completed / p.total * 100 : 0;
                        });
                    }),
                    token);
            }
            else if (pairs.Count == 1)
            {
                var (src, dest) = pairs[0];
                StatusText.Text = $"Copying {Path.GetFileName(src)}...";
                await _driveService.CopyFileFromLocalAsync(
                    src,
                    dest,
                    new Progress<(long copied, long total)>(p =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            OperationProgress.Value = p.total > 0 ? (double)p.copied / p.total * 100 : 0;
                        });
                    }),
                    token);
            }

            StatusText.Text = $"Copied {pairs.Count} file{(pairs.Count != 1 ? "s" : "")}";
            await LoadDirectoryAsync(_currentPath);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Copy cancelled";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Copy error: {ex.Message}";
        }
        finally
        {
            HideOperationProgress();
            _operationCts = null;
        }
    }

    private async void NewFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "New Folder",
            Content = new TextBox { PlaceholderText = "Folder name" },
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            var nameBox = dialog.Content as TextBox;
            var name = nameBox?.Text?.Trim();

            if (!string.IsNullOrEmpty(name))
            {
                try
                {
                    var newPath = _currentPath.TrimEnd('/') + "/" + name;
                    _driveService.CreateDirectory(newPath);
                    StatusText.Text = $"Created folder: {name}";
                    await LoadDirectoryAsync(_currentPath);
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Error creating folder: {ex.Message}";
                }
            }
        }
    }

    private async void RenameMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is not FileSystemEntry entry)
        {
            return;
        }

        var nameBox = new TextBox { Text = entry.Name };

        var dialog = new ContentDialog
        {
            Title = "Rename",
            Content = nameBox,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            var newName = nameBox.Text?.Trim();

            if (!string.IsNullOrEmpty(newName) && newName != entry.Name)
            {
                try
                {
                    var parentPath = Path.GetDirectoryName(entry.FullPath.Replace('/', Path.DirectorySeparatorChar))
                        ?.Replace(Path.DirectorySeparatorChar, '/') ?? "/";
                    var newPath = parentPath.TrimEnd('/') + "/" + newName;

                    _driveService.Move(entry.FullPath, newPath);
                    StatusText.Text = $"Renamed to: {newName}";
                    await LoadDirectoryAsync(_currentPath);
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Error renaming: {ex.Message}";
                }
            }
        }
    }

    private async void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = FileListView.SelectedItems.Cast<FileSystemEntry>().ToList();
        if (selectedItems.Count == 0)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Delete",
            Content = $"Are you sure you want to delete {selectedItems.Count} item{(selectedItems.Count != 1 ? "s" : "")}?\n\nThis action cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            try
            {
                foreach (var entry in selectedItems)
                {
                    if (entry.IsDirectory)
                    {
                        _driveService.DeleteDirectory(entry.FullPath);
                    }
                    else
                    {
                        _driveService.DeleteFile(entry.FullPath);
                    }
                }

                StatusText.Text = $"Deleted {selectedItems.Count} item{(selectedItems.Count != 1 ? "s" : "")}";
                await LoadDirectoryAsync(_currentPath);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error deleting: {ex.Message}";
            }
        }
    }

    private async void PropertiesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is not FileSystemEntry entry)
        {
            return;
        }

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = $"Name: {entry.Name}" });
        content.Children.Add(new TextBlock { Text = $"Type: {(entry.IsDirectory ? "Folder" : "File")}" });
        content.Children.Add(new TextBlock { Text = $"Path: {entry.FullPath}" });
        content.Children.Add(new TextBlock { Text = $"Size: {(entry.IsDirectory ? "-" : FormatSize(entry.Size))}" });
        content.Children.Add(new TextBlock { Text = $"Modified: {entry.LastModified:yyyy-MM-dd HH:mm:ss}" });
        if (entry.CreationTime.HasValue)
        {
            content.Children.Add(new TextBlock { Text = $"Created: {entry.CreationTime:yyyy-MM-dd HH:mm:ss}" });
        }

        content.Children.Add(new TextBlock { Text = $"Permissions: {entry.Permissions}" });

        var dialog = new ContentDialog
        {
            Title = "Properties",
            Content = content,
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot
        };

        await dialog.ShowAsync();
    }

    #endregion

    #region UI Helpers

    private void ShowLoading(string message)
    {
        LoadingText.Text = message;
        LoadingPanel.Visibility = Visibility.Visible;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        FileListView.Visibility = Visibility.Collapsed;
    }

    private void HideLoading()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowEmptyState()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        EmptyStatePanel.Visibility = Visibility.Visible;
        FileListView.Visibility = Visibility.Collapsed;
    }

    private void ShowOperationProgress()
    {
        OperationProgress.Visibility = Visibility.Visible;
        OperationProgress.Value = 0;
        CancelOperationButton.Visibility = Visibility.Visible;
    }

    private void HideOperationProgress()
    {
        OperationProgress.Visibility = Visibility.Collapsed;
        CancelOperationButton.Visibility = Visibility.Collapsed;
    }

    private void CancelOperationButton_Click(object sender, RoutedEventArgs e)
    {
        _operationCts?.Cancel();
    }

    private static string FormatSize(long bytes)
    {
        return FormatSize((ulong)Math.Max(0, bytes));
    }

    private static string FormatSize(ulong bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    #endregion
}
