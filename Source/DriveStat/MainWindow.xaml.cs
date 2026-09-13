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

    public MainWindow()
    {
        InitializeComponent();
        TrySetMicaBackdrop();
        TrySetExtendedTitleBar();
        TrySetWindowSize(1400, 800);
        ConfigureElevationBanner();
        LoadDrives();
        Treemap.RevealInExplorerRequested += Treemap_RevealInExplorerRequested;
        Treemap.FileInfoRequested += Treemap_FileInfoRequested;
        TreemapScrollViewer.PointerWheelChanged += TreemapScrollViewer_PointerWheelChanged;
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
            // Mica not supported
        }
    }

    private void TrySetExtendedTitleBar()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
        }
        catch { }
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
        catch { }
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
            _root = await ScanService.ScanAsync(path, progress, token);
            _extensions = ScanService.BuildExtensionList(_root);
            _extensionColorMap = _extensions.ToDictionary(ex => ex.Extension, ex => ex.Color);

            PopulateTree();
            ExtensionList.ItemsSource = _extensions;
            Treemap.Root = _root;
            Treemap.TreeRoot = _root;
            Treemap.ExtensionColors = _extensionColorMap;
            Treemap.ExtensionFilter = null;
            ShowAllButton.Visibility = Visibility.Collapsed;

            HeatMapButton.IsEnabled = true;
            UpdateTreemapBackButtonVisibility();
            StatusText.Text = $"Scan complete: {DirectoryNode.FormatSize(_root.Size)} in {_extensions.Sum(x => x.FileCount):N0} files.";
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
        DirectoryNode? dn = null;
        if (DirectoryTree.SelectedItem is TreeViewNode tvn && tvn.Content is DirectoryNode d)
        {
            dn = d;
        }

        if (dn != null)
        {
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
