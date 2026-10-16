namespace DriveStat;

/// <summary>
/// Main window for DriveStat: disk usage statistics with directory tree, treemap, and extension list.
/// </summary>
public sealed partial class MainWindow : Window
{
    private DirectoryNode? _root;
    private List<ExtensionInfo> _extensions = [];
    private IReadOnlyDictionary<string, Windows.UI.Color> _extensionColorMap = new Dictionary<string, Windows.UI.Color>();
    private CancellationTokenSource? _scanCts;

    public MainWindow(string? initialPath = null)
    {
        InitializeComponent();
        WindowChrome.TryApplyMica(this);
        WindowChrome.TryExtendTitleBar(this, AppTitleBar);
        WindowChrome.TrySetSize(this, 1400, 800);
        ConfigureElevationBanner();
        LoadDrives();
        Treemap.RevealInExplorerRequested += Treemap_RevealInExplorerRequested;
        Treemap.FileInfoRequested += Treemap_FileInfoRequested;
        TreemapScrollViewer.PointerWheelChanged += TreemapScrollViewer_PointerWheelChanged;

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            string path = initialPath.Trim().Trim('"');
            if (Directory.Exists(path))
            {
                string trimmed = path.TrimEnd('\\', '/');
                string name = Path.GetFileName(trimmed);
                if (string.IsNullOrEmpty(name))
                {
                    name = trimmed;
                }

                DriveCombo.Items.Insert(0, new DriveComboItem(name, path, path));
                DriveCombo.SelectedIndex = 0;
                DispatcherQueue.TryEnqueue(() => ScanButton_Click(ScanButton, new RoutedEventArgs()));
            }
        }
    }

    private void ConfigureElevationBanner()
    {
        ElevationUi.StyleWarningBanner(ElevationBanner);
        ElevationUi.ConfigureBanner(
            ElevationBanner,
            ElevationBannerText,
            "Not running as Administrator. Some system folders may be skipped during scans.",
            RestartAsAdminButton,
            null,
            "Restart as Administrator to scan protected folders with fewer access errors.");
    }

    private void LoadDrives()
    {
        DriveCombo.Items.Clear();
        foreach (var drive in ScanService.GetDrives())
        {
            var item = new DriveComboItem(drive.Name, drive.RootDirectory.FullName, $"{drive.Name} ({drive.VolumeLabel})");
            DriveCombo.Items.Add(item);
        }
        DriveCombo.SelectedIndex = 0;
    }

    private void DriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selection ready for scan
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        var hwnd = WindowNative.GetWindowHandle(this);
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            var item = new DriveComboItem(folder.Name, folder.Path, folder.Path);
            DriveCombo.Items.Insert(0, item);
            DriveCombo.SelectedIndex = 0;
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        string? path = null;
        if (DriveCombo.SelectedItem is DriveComboItem item)
        {
            path = item.Path;
        }

        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            StatusText.Text = "Please select a valid drive or folder.";
            return;
        }

        SetScanning(true);
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;
        var progress = new Progress<ScanProgress>(p =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                StatusText.Text = $"{DirectoryNode.FormatSize(p.TotalBytes)} in {p.FileCount:N0} files — {p.CurrentPath}";
            });
        });

        try
        {
            await ApplyScanResultAsync(path, progress, token);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            SetScanning(false);
        }
    }

    private async Task ApplyScanResultAsync(string path, IProgress<ScanProgress> progress, CancellationToken token)
    {
        _root = await ScanService.ScanAsync(path, progress, token);
        var preferred = ExtensionColorStore.Load();
        _extensions = ScanService.BuildExtensionList(_root, preferred);
        ExtensionColorStore.Save(_extensions);
        _extensionColorMap = _extensions.ToDictionary(ex => ex.Extension, ex => ex.Color);

        PopulateTree();
        ExtensionList.ItemsSource = _extensions;
        Treemap.Root = _root;
        Treemap.TreeRoot = _root;
        Treemap.ExtensionColors = _extensionColorMap;
        Treemap.ExtensionFilter = null;
        ShowAllButton.Visibility = Visibility.Collapsed;

        HeatMapButton.IsEnabled = true;
        ExportButton.IsEnabled = true;
        UpdateTreemapBackButtonVisibility();
        StatusText.Text = $"Scan complete: {DirectoryNode.FormatSize(_root.Size)} in {_extensions.Sum(x => x.FileCount):N0} files.";
    }

    private void HeatMapButton_Click(object sender, RoutedEventArgs e)
    {
        if (_root == null)
        {
            StatusText.Text = "Scan a drive or folder first.";
            return;
        }
        var heatMapWindow = new HeatMapWindow(_root);
        heatMapWindow.Activate();
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_root == null)
        {
            StatusText.Text = "Scan a drive or folder first.";
            return;
        }

        var picker = new Windows.Storage.Pickers.FileSavePicker();
        var hwnd = WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedFileName = "DriveStat-report";
        picker.FileTypeChoices.Add("CSV report", [".csv"]);
        picker.FileTypeChoices.Add("HTML report", [".html"]);

        var file = await picker.PickSaveFileAsync();
        if (file == null)
        {
            return;
        }

        try
        {
            string content = file.FileType.Equals(".html", StringComparison.OrdinalIgnoreCase)
                ? ExportService.BuildHtml(_root, _extensions)
                : ExportService.BuildCsv(_root, _extensions);
            await Windows.Storage.FileIO.WriteTextAsync(file, content);
            StatusText.Text = $"Exported report to {file.Path}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Export failed: {ex.Message}";
            AppLog.Error("DriveStat export failed.", ex, nameof(MainWindow));
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _scanCts?.Cancel();
    }

    private void SetScanning(bool scanning)
    {
        ScanButton.IsEnabled = !scanning;
        BrowseButton.IsEnabled = !scanning;
        DriveCombo.IsEnabled = !scanning;
        ProgressBar.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PopulateTree()
    {
        DirectoryTree.RootNodes.Clear();
        if (_root == null) return;

        var rootNode = CreateTreeViewNode(_root);
        DirectoryTree.RootNodes.Add(rootNode);
        rootNode.IsExpanded = true;
    }

    private static TreeViewNode CreateTreeViewNode(DirectoryNode dir)
    {
        var node = new TreeViewNode { Content = dir };
        foreach (var child in dir.Children)
        {
            node.Children.Add(CreateTreeViewNode(child));
        }
        return node;
    }

    private void ExtensionList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ExtensionInfo ext)
        {
            return;
        }
        var currentFilter = Treemap.ExtensionFilter;
        if (string.Equals(currentFilter, ext.Extension, StringComparison.OrdinalIgnoreCase))
        {
            Treemap.ExtensionFilter = null;
            ShowAllButton.Visibility = Visibility.Collapsed;
            ExtensionList.SelectedItem = null;
        }
        else
        {
            Treemap.ExtensionFilter = ext.Extension;
            ShowAllButton.Visibility = Visibility.Visible;
            ExtensionList.SelectedItem = ext;
        }
    }

    private void ShowAllButton_Click(object sender, RoutedEventArgs e)
    {
        Treemap.ExtensionFilter = null;
        ShowAllButton.Visibility = Visibility.Collapsed;
        ExtensionList.SelectedItem = null;
    }

    private void DirectoryTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (args.AddedItems.Count > 0 && args.AddedItems[0] is TreeViewNode tvn && tvn.Content is DirectoryNode dn)
        {
            Treemap.Root = dn;
            Treemap.ExtensionColors = _extensionColorMap;
        }
        UpdateTreemapBackButtonVisibility();
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        var sv = TreemapScrollViewer;
        var newZoom = Math.Min(sv.ZoomFactor + 0.25, sv.MaxZoomFactor);
        sv.ZoomToFactor((float)newZoom);
        UpdateZoomLabel();
    }

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
    {
        var sv = TreemapScrollViewer;
        var newZoom = Math.Max(sv.ZoomFactor - 0.25, sv.MinZoomFactor);
        sv.ZoomToFactor((float)newZoom);
        UpdateZoomLabel();
    }

    private void TreemapScrollViewer_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (e.KeyModifiers == Windows.System.VirtualKeyModifiers.Control)
        {
            var delta = e.GetCurrentPoint(TreemapScrollViewer).Properties.MouseWheelDelta;
            if (delta != 0)
            {
                var sv = TreemapScrollViewer;
                var factor = delta > 0 ? 1.25 : 0.8;
                var newZoom = Math.Clamp(sv.ZoomFactor * factor, sv.MinZoomFactor, sv.MaxZoomFactor);
                sv.ZoomToFactor((float)newZoom);
                UpdateZoomLabel();
                e.Handled = true;
            }
        }
    }

    private void TreemapScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!e.IsIntermediate)
        {
            UpdateZoomLabel();
        }
    }

    private void UpdateZoomLabel()
    {
        if (ZoomLabel != null)
        {
            ZoomLabel.Text = $"{(int)(TreemapScrollViewer.ZoomFactor * 100)}%";
        }
    }

    private async void Treemap_RevealInExplorerRequested(object? sender, DirectoryNode node)
    {
        try
        {
            if (node.IsDirectory && Directory.Exists(node.FullPath))
            {
                await Windows.System.Launcher.LaunchFolderPathAsync(node.FullPath);
            }
            else if (!node.IsDirectory && File.Exists(node.FullPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{node.FullPath}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not open Explorer: {ex.Message}";
        }
    }

    private void Treemap_FileInfoRequested(object? sender, DirectoryNode node)
    {
        var infoWindow = new FileInfoWindow(node);
        infoWindow.Activate();
    }

    private void Treemap_SelectionChanged(object? sender, DirectoryNode? node)
    {
        if (node != null)
        {
            SelectTreeNode(DirectoryTree.RootNodes, node);
        }
        UpdateTreemapBackButtonVisibility();
    }

    private void TreemapBackButton_Click(object sender, RoutedEventArgs e)
    {
        var current = Treemap.Root;
        if (current?.Parent != null)
        {
            Treemap.Root = current.Parent;
            Treemap.SelectedNode = current.Parent;
            SelectTreeNode(DirectoryTree.RootNodes, current.Parent);
        }
        else if (current != Treemap.TreeRoot && Treemap.TreeRoot != null)
        {
            Treemap.Root = Treemap.TreeRoot;
            Treemap.SelectedNode = Treemap.TreeRoot;
            SelectTreeNode(DirectoryTree.RootNodes, Treemap.TreeRoot);
        }
        UpdateTreemapBackButtonVisibility();
    }

    private void UpdateTreemapBackButtonVisibility()
    {
        var root = Treemap.Root;
        var treeRoot = Treemap.TreeRoot;
        TreemapBackButton.Visibility = (root != null && treeRoot != null && root != treeRoot) ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool SelectTreeNode(IList<TreeViewNode> nodes, DirectoryNode target)
    {
        foreach (var n in nodes)
        {
            if (n.Content is DirectoryNode dn && dn == target)
            {
                n.IsExpanded = true;
                return true;
            }
            if (SelectTreeNode(n.Children, target))
            {
                n.IsExpanded = true;
                return true;
            }
        }
        return false;
    }

    private async void OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        DirectoryNode? dn = GetContextDirectoryNode(sender);
        if (dn == null)
        {
            return;
        }

        if (dn.IsDirectory && Directory.Exists(dn.FullPath))
        {
            await Windows.System.Launcher.LaunchFolderPathAsync(dn.FullPath);
        }
        else if (!dn.IsDirectory && File.Exists(dn.FullPath))
        {
            var uri = new Uri(dn.FullPath.StartsWith("\\\\") ? $"file:{dn.FullPath.Replace("\\", "/")}" : $"file:///{dn.FullPath.Replace("\\", "/")}");
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    private void ScanFromHere_Click(object sender, RoutedEventArgs e)
    {
        DirectoryNode? dn = GetContextDirectoryNode(sender);
        if (dn == null || !dn.IsDirectory || !Directory.Exists(dn.FullPath))
        {
            StatusText.Text = "Select a folder to scan from here.";
            return;
        }

        var item = new DriveComboItem(dn.Name, dn.FullPath, dn.FullPath);
        DriveCombo.Items.Insert(0, item);
        DriveCombo.SelectedIndex = 0;
        ScanButton_Click(ScanButton, new RoutedEventArgs());
    }

    private void OpenHandleFinder_Click(object sender, RoutedEventArgs e)
    {
        DirectoryNode? dn = GetContextDirectoryNode(sender);
        if (dn == null || string.IsNullOrWhiteSpace(dn.FullPath))
        {
            StatusText.Text = "Select an item first.";
            return;
        }

        if (!SuiteApps.TryLaunchHandleFinderGui(dn.FullPath))
        {
            StatusText.Text = "Could not start HandleFinder. Build the suite so each app has its own output folder.";
            return;
        }

        StatusText.Text = "Started HandleFinder for this path.";
    }

    private async void MoveItem_Click(object sender, RoutedEventArgs e)
    {
        DirectoryNode? dn = GetContextDirectoryNode(sender);
        if (dn == null)
        {
            StatusText.Text = "Select an item in the tree first.";
            return;
        }

        if (_root != null && string.Equals(dn.FullPath, _root.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "Cannot move the scan root from here.";
            return;
        }

        var picker = new Windows.Storage.Pickers.FolderPicker();
        var hwnd = WindowNative.GetWindowHandle(this);
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder == null)
        {
            return;
        }

        string dest = Path.Combine(folder.Path, dn.Name);
        if (!await DestructiveConfirm.ConfirmAsync(
                Content.XamlRoot,
                "Move item?",
                $"Move '{dn.Name}' to:\n{dest}",
                "Move"))
        {
            return;
        }

        try
        {
            if (dn.IsDirectory)
            {
                Directory.Move(dn.FullPath, dest);
            }
            else
            {
                File.Move(dn.FullPath, dest);
            }

            RemoveNodeFromTree(dn);
            StatusText.Text = $"Moved to {dest}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Move failed: {ex.Message}";
            AppLog.Warning("DriveStat move failed.", ex, nameof(MainWindow));
        }
    }

    private async void RecycleItem_Click(object sender, RoutedEventArgs e)
        => await DeleteSelectedAsync(sender, recycle: true);

    private async void DeleteItem_Click(object sender, RoutedEventArgs e)
        => await DeleteSelectedAsync(sender, recycle: false);

    private async Task DeleteSelectedAsync(object sender, bool recycle)
    {
        DirectoryNode? dn = GetContextDirectoryNode(sender);
        if (dn == null)
        {
            StatusText.Text = "Select an item in the tree first.";
            return;
        }

        if (_root != null && string.Equals(dn.FullPath, _root.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "Cannot delete the scan root from here. Choose a child item.";
            return;
        }

        string title = recycle ? "Move to Recycle Bin?" : "Delete permanently?";
        string message = recycle
            ? $"Move '{dn.Name}' to the Recycle Bin?\n\n{dn.FullPath}"
            : $"Permanently delete '{dn.Name}'?\n\n{dn.FullPath}\n\nThis cannot be undone.";
        string primary = recycle ? "Move to Recycle Bin" : "Delete permanently";

        bool ok = recycle
            ? await DestructiveConfirm.ConfirmAsync(Content.XamlRoot, title, message, primary)
            : await DestructiveConfirm.ConfirmWithAcknowledgementAsync(Content.XamlRoot, title, message, primary);

        if (!ok)
        {
            return;
        }

        bool success = recycle ? FileActions.TryRecycle(dn.FullPath) : FileActions.TryDeletePermanent(dn.FullPath);
        if (!success)
        {
            StatusText.Text = recycle
                ? "Could not move to Recycle Bin. The item may be in use or protected."
                : "Could not delete. The item may be in use or protected.";
            return;
        }

        RemoveNodeFromTree(dn);
        StatusText.Text = recycle ? $"Moved to Recycle Bin: {dn.Name}" : $"Deleted: {dn.Name}";
    }

    private DirectoryNode? GetContextDirectoryNode(object sender)
    {
        if (sender is MenuFlyoutItem flyoutItem &&
            flyoutItem.Parent is MenuFlyout { Target: FrameworkElement target })
        {
            if (target.DataContext is TreeViewNode tvn && tvn.Content is DirectoryNode fromTarget)
            {
                return fromTarget;
            }

            if (target.DataContext is DirectoryNode direct)
            {
                return direct;
            }
        }

        if (DirectoryTree.SelectedItem is TreeViewNode selected && selected.Content is DirectoryNode dn)
        {
            return dn;
        }

        return null;
    }

    private void RemoveNodeFromTree(DirectoryNode dn)
    {
        DirectoryNode? refreshRoot = Treemap.Root;
        if (dn.Parent != null)
        {
            dn.Parent.Children.Remove(dn);
            for (DirectoryNode? p = dn.Parent; p != null; p = p.Parent)
            {
                p.Size = p.Children.Sum(c => c.IsHardLinkDuplicate ? 0 : c.Size);
            }
        }

        if (_root != null)
        {
            var preferred = ExtensionColorStore.Load();
            _extensions = ScanService.BuildExtensionList(_root, preferred);
            ExtensionColorStore.Save(_extensions);
            _extensionColorMap = _extensions.ToDictionary(ex => ex.Extension, ex => ex.Color);
            ExtensionList.ItemsSource = _extensions;
            Treemap.ExtensionColors = _extensionColorMap;
            Treemap.TreeRoot = _root;
            Treemap.Root = refreshRoot != null && refreshRoot != dn ? refreshRoot : _root;
        }

        PopulateTree();
        UpdateTreemapBackButtonVisibility();
    }
}

/// <summary>
/// Item for the drive/folder combo box.
/// </summary>
public sealed class DriveComboItem
{
    public string Name { get; }
    public string Path { get; }
    public string DisplayText { get; }

    public DriveComboItem(string name, string path, string displayText)
    {
        Name = name;
        Path = path;
        DisplayText = displayText;
    }

    public override string ToString() => DisplayText;
}
