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
        LoadDrives();
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
    }

    private void Treemap_SelectionChanged(object? sender, DirectoryNode? node)
    {
        if (node != null)
        {
            SelectTreeNode(DirectoryTree.RootNodes, node);
        }
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
