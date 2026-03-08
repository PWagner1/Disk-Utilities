namespace DriveStat.Controls;

/// <summary>
/// Custom control that renders a squarified treemap of directory/file sizes.
/// </summary>
public sealed partial class TreemapControl : UserControl
{
    private DirectoryNode? _root;
    private DirectoryNode? _treeRoot;
    private IReadOnlyDictionary<string, Windows.UI.Color> _extensionColors = new Dictionary<string, Windows.UI.Color>();
    private const double MinRectSize = 4;
    private const double RectPadding = 1;
    private const double MinLabelSize = 36; // Minimum block size to show file name

    public static readonly DependencyProperty RootProperty = DependencyProperty.Register(
        nameof(Root), typeof(DirectoryNode), typeof(TreemapControl),
        new PropertyMetadata(null, OnRootChanged));

    public static readonly DependencyProperty ExtensionColorsProperty = DependencyProperty.Register(
        nameof(ExtensionColors), typeof(IReadOnlyDictionary<string, Windows.UI.Color>), typeof(TreemapControl),
        new PropertyMetadata(new Dictionary<string, Windows.UI.Color>(), OnExtensionColorsChanged));

    public static readonly DependencyProperty SelectedNodeProperty = DependencyProperty.Register(
        nameof(SelectedNode), typeof(DirectoryNode), typeof(TreemapControl),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ExtensionFilterProperty = DependencyProperty.Register(
        nameof(ExtensionFilter), typeof(string), typeof(TreemapControl),
        new PropertyMetadata(null, OnExtensionFilterChanged));

    public static readonly DependencyProperty TreeRootProperty = DependencyProperty.Register(
        nameof(TreeRoot), typeof(DirectoryNode), typeof(TreemapControl),
        new PropertyMetadata(null, OnTreeRootChanged));

    public DirectoryNode? Root
    {
        get => (DirectoryNode?)GetValue(RootProperty);
        set => SetValue(RootProperty, value);
    }

    public IReadOnlyDictionary<string, Windows.UI.Color> ExtensionColors
    {
        get => (IReadOnlyDictionary<string, Windows.UI.Color>)GetValue(ExtensionColorsProperty);
        set => SetValue(ExtensionColorsProperty, value);
    }

    public DirectoryNode? SelectedNode
    {
        get => (DirectoryNode?)GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    /// <summary>When set, only files with this extension are shown. Use "(no extension)" for files without extension. Null = show all.</summary>
    public string? ExtensionFilter
    {
        get => (string?)GetValue(ExtensionFilterProperty);
        set => SetValue(ExtensionFilterProperty, value);
    }

    /// <summary>Full tree root for extension filtering. When filtering, files are collected from this. If null, Root is used.</summary>
    public DirectoryNode? TreeRoot
    {
        get => (DirectoryNode?)GetValue(TreeRootProperty);
        set => SetValue(TreeRootProperty, value);
    }

    public event EventHandler<DirectoryNode?>? SelectionChanged;

    /// <summary>Raised when the user chooses "Reveal in Explorer" from the context menu.</summary>
    public event EventHandler<DirectoryNode>? RevealInExplorerRequested;

    /// <summary>Raised when the user chooses "More Information" from the context menu.</summary>
    public event EventHandler<DirectoryNode>? FileInfoRequested;

    public TreemapControl()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Render();
    }

    private static void OnRootChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreemapControl ctrl)
        {
            ctrl._root = (DirectoryNode?)e.NewValue;
            ctrl._treeRoot ??= ctrl._root;
            ctrl.Render();
        }
    }

    private static void OnExtensionColorsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreemapControl ctrl)
        {
            ctrl._extensionColors = (IReadOnlyDictionary<string, Windows.UI.Color>)(e.NewValue ?? new Dictionary<string, Windows.UI.Color>());
            ctrl.Render();
        }
    }

    private static void OnExtensionFilterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreemapControl ctrl)
        {
            ctrl.Render();
        }
    }

    private static void OnTreeRootChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreemapControl ctrl)
        {
            ctrl._treeRoot = (DirectoryNode?)e.NewValue;
            ctrl.Render();
        }
    }

    private void Render()
    {
        try
        {
            TreemapCanvas.Children.Clear();
            if (_root == null || _root.Size <= 0)
            {
                return;
            }

            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0)
            {
                return;
            }

            var items = GetFilteredChildren();
            if (items.Count == 0)
            {
                return;
            }

        // Calculate min/max sizes for height normalization
        long minSize = items.Min(i => i.Size);
        long maxSize = items.Max(i => i.Size);
        if (maxSize <= minSize)
        {
            maxSize = minSize + 1;
        }

        // Add extra space for 3D effects
        TreemapCanvas.Width = w + 100;
        TreemapCanvas.Height = h + 100;

        var rects = Squarify(items, 0, 0, w, h);
        
        // Isometric projection: 30-degree rotation, scale factors
        double isoAngle = 30.0 * Math.PI / 180.0;
        double isoCos = Math.Cos(isoAngle); // ~0.866
        double isoSin = Math.Sin(isoAngle); // 0.5
        
        foreach (var (node, rect) in rects)
        {
            var baseWidth = Math.Max(0, rect.Width - RectPadding * 2);
            var baseDepth = Math.Max(0, rect.Height - RectPadding * 2);
            
            // Skip blocks that are too small to render
            if (baseWidth < 2 || baseDepth < 2)
            {
                continue;
            }
            
            var color = GetColor(node);

            // Calculate height based on size (normalized 0-1, then scaled)
            double heightNormalized = maxSize > minSize 
                ? (double)(node.Size - minSize) / (maxSize - minSize) 
                : 0.5;
            double boxHeight = 10 + heightNormalized * 80; // Height ranges from 10 to 90 pixels

            // Base position
            double baseX = rect.X + RectPadding + 50;
            double baseY = rect.Y + RectPadding + 50;
            
            // Create container for the 3D box
            var container = new Canvas
            {
                Tag = node
            };

            // Create darker shades for side faces (simulate lighting)
            var frontColor = Windows.UI.Color.FromArgb(255, 
                (byte)Math.Max(0, color.R - 35), 
                (byte)Math.Max(0, color.G - 35), 
                (byte)Math.Max(0, color.B - 35));
            var rightColor = Windows.UI.Color.FromArgb(255, 
                (byte)Math.Max(0, color.R - 55), 
                (byte)Math.Max(0, color.G - 55), 
                (byte)Math.Max(0, color.B - 55));

            // Calculate isometric offsets
            double isoOffsetX = boxHeight * isoSin; // Horizontal offset for depth
            double isoOffsetY = boxHeight * isoCos; // Vertical offset for height
            
            // Validate calculations to prevent NaN/Infinity
            if (double.IsNaN(baseX) || double.IsNaN(baseY) || double.IsNaN(isoOffsetX) || double.IsNaN(isoOffsetY) ||
                double.IsInfinity(baseX) || double.IsInfinity(baseY) || double.IsInfinity(isoOffsetX) || double.IsInfinity(isoOffsetY))
            {
                continue;
            }

            // Draw front face (vertical rectangle facing viewer)
            var frontFace = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = baseWidth,
                Height = boxHeight,
                Fill = new SolidColorBrush(frontColor),
                Stroke = new SolidColorBrush(Colors.Black) { Opacity = 0.15 },
                StrokeThickness = 0.5
            };
            Canvas.SetLeft(frontFace, baseX);
            Canvas.SetTop(frontFace, baseY + baseDepth - isoOffsetY);
            container.Children.Add(frontFace);

            // Draw right side face (vertical parallelogram, right side)
            var rightFacePath = new Microsoft.UI.Xaml.Shapes.Path
            {
                Fill = new SolidColorBrush(rightColor),
                Stroke = new SolidColorBrush(Colors.Black) { Opacity = 0.15 },
                StrokeThickness = 0.5
            };
            var rightFaceGeometry = new PathGeometry();
            var rightFaceFigure = new PathFigure
            {
                StartPoint = new Windows.Foundation.Point(baseX + baseWidth, baseY + baseDepth - isoOffsetY),
                IsClosed = true
            };
            rightFaceFigure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(baseX + baseWidth + isoOffsetX, baseY + baseDepth - isoOffsetY - isoOffsetY) });
            rightFaceFigure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(baseX + baseWidth + isoOffsetX, baseY + baseDepth - isoOffsetY - isoOffsetY + boxHeight) });
            rightFaceFigure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(baseX + baseWidth, baseY + baseDepth - isoOffsetY + boxHeight) });
            rightFaceGeometry.Figures.Add(rightFaceFigure);
            rightFacePath.Data = rightFaceGeometry;
            container.Children.Add(rightFacePath);

            // Draw top face (horizontal parallelogram, visible top)
            var lighterColor = Windows.UI.Color.FromArgb(255, 
                (byte)Math.Min(255, color.R + 35), 
                (byte)Math.Min(255, color.G + 35), 
                (byte)Math.Min(255, color.B + 35));
            var topGradient = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1)
            };
            topGradient.GradientStops.Add(new GradientStop { Color = lighterColor, Offset = 0 });
            topGradient.GradientStops.Add(new GradientStop { Color = color, Offset = 1 });

            var topFacePath = new Microsoft.UI.Xaml.Shapes.Path
            {
                Fill = topGradient,
                Stroke = new SolidColorBrush(Colors.Black) { Opacity = 0.15 },
                StrokeThickness = 0.5
            };
            var topFaceGeometry = new PathGeometry();
            var topFaceFigure = new PathFigure
            {
                StartPoint = new Windows.Foundation.Point(baseX, baseY + baseDepth - isoOffsetY),
                IsClosed = true
            };
            topFaceFigure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(baseX + baseWidth, baseY + baseDepth - isoOffsetY) });
            topFaceFigure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(baseX + baseWidth + isoOffsetX, baseY + baseDepth - isoOffsetY - isoOffsetY) });
            topFaceFigure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(baseX + isoOffsetX, baseY + baseDepth - isoOffsetY - isoOffsetY) });
            topFaceGeometry.Figures.Add(topFaceFigure);
            topFacePath.Data = topFaceGeometry;
            container.Children.Add(topFacePath);

            // Add label on top face if large enough
            var showLabel = baseWidth >= MinLabelSize && baseDepth >= MinLabelSize;
            if (showLabel)
            {
                var labelPanel = new StackPanel
                {
                    MaxWidth = baseWidth - 8,
                    MaxHeight = baseDepth - 8
                };
                var nameText = new TextBlock
                {
                    Text = node.Name,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    Foreground = new SolidColorBrush(Colors.White),
                    FontSize = Math.Max(8, Math.Min(11, Math.Min(baseWidth, baseDepth) * 0.2)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
                labelPanel.Children.Add(nameText);
                if (baseDepth >= MinLabelSize * 1.5)
                {
                    var sizeText = new TextBlock
                    {
                        Text = DirectoryNode.FormatSize(node.Size),
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 230, 230, 230)),
                        FontSize = Math.Max(7, Math.Min(9, Math.Min(baseWidth, baseDepth) * 0.15)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Opacity = 0.9
                    };
                    labelPanel.Children.Add(sizeText);
                }
                var labelBorder = new Border
                {
                    Child = labelPanel,
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(160, 0, 0, 0)),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(4, 2, 4, 2)
                };
                Canvas.SetLeft(labelBorder, baseX + isoOffsetX * 0.3 + 4);
                Canvas.SetTop(labelBorder, baseY + baseDepth - isoOffsetY - isoOffsetY * 0.5 + 4);
                container.Children.Add(labelBorder);
            }

            // Position the container
            Canvas.SetLeft(container, 0);
            Canvas.SetTop(container, 0);

            container.PointerEntered += (s, _) =>
            {
                if (s is FrameworkElement fe && fe.Tag is DirectoryNode n)
                {
                    ToolTipService.SetToolTip(fe, $"{n.Name}\n{DirectoryNode.FormatSize(n.Size)}\nHeight: {boxHeight:F0}px");
                }
            };
            container.PointerPressed += (s, _) =>
            {
                if (s is FrameworkElement fe && fe.Tag is DirectoryNode n)
                {
                    SelectedNode = n;
                    SelectionChanged?.Invoke(this, n);
                    if (n.IsDirectory && n.Children.Count > 0)
                    {
                        Root = n;
                    }
                }
            };

            container.ContextFlyout = CreateContextMenu(node);

            TreemapCanvas.Children.Add(container);
        }
        }
        catch (Exception ex)
        {
            // Log or handle rendering errors gracefully
            System.Diagnostics.Debug.WriteLine($"Treemap rendering error: {ex.Message}");
        }
    }

    private List<DirectoryNode> GetFilteredChildren()
    {
        var filter = ExtensionFilter;
        if (string.IsNullOrEmpty(filter))
        {
            return _root!.Children;
        }
        var matchNoExt = string.Equals(filter, "(no extension)", StringComparison.OrdinalIgnoreCase);
        var filterLower = filter.ToLowerInvariant();
        var result = new List<DirectoryNode>();
        var source = _treeRoot ?? _root;
        if (source != null)
        {
            CollectMatchingFiles(source, result, filterLower, matchNoExt);
        }
        return result;
    }

    private static void CollectMatchingFiles(DirectoryNode node, List<DirectoryNode> result, string filterLower, bool matchNoExt)
    {
        foreach (var child in node.Children)
        {
            if (child.IsDirectory)
            {
                CollectMatchingFiles(child, result, filterLower, matchNoExt);
            }
            else
            {
                var ext = string.IsNullOrEmpty(child.Extension) ? "(no extension)" : child.Extension.ToLowerInvariant();
                if (matchNoExt ? string.IsNullOrEmpty(child.Extension) : ext == filterLower)
                {
                    result.Add(child);
                }
            }
        }
    }

    private MenuFlyout CreateContextMenu(DirectoryNode node)
    {
        var revealItem = new MenuFlyoutItem { Text = "Reveal in Explorer" };
        revealItem.Click += (_, _) => RevealInExplorerRequested?.Invoke(this, node);

        var infoItem = new MenuFlyoutItem { Text = "More Information" };
        infoItem.Click += (_, _) => FileInfoRequested?.Invoke(this, node);

        var flyout = new MenuFlyout();
        flyout.Items.Add(revealItem);
        flyout.Items.Add(infoItem);
        return flyout;
    }

    private Windows.UI.Color GetColor(DirectoryNode node)
    {
        if (node.IsDirectory)
        {
            return Windows.UI.Color.FromArgb(255, 96, 125, 139); // Blue grey for folders
        }

        var ext = string.IsNullOrEmpty(node.Extension) ? "(no extension)" : node.Extension.ToLowerInvariant();
        return _extensionColors.TryGetValue(ext, out var c) ? c : Windows.UI.Color.FromArgb(255, 158, 158, 158);
    }

    /// <summary>
    /// Slice-and-dice treemap layout. Divides the rectangle proportionally by size.
    /// </summary>
    private static List<(DirectoryNode Node, Windows.Foundation.Rect Rect)> Squarify(List<DirectoryNode> items, double x, double y, double w, double h)
    {
        var result = new List<(DirectoryNode, Windows.Foundation.Rect)>();
        if (items.Count == 0 || w <= 0 || h <= 0)
        {
            return result;
        }

        long total = items.Sum(i => i.Size);
        if (total <= 0)
        {
            return result;
        }

        bool horizontal = w >= h;
        double offset = 0;

        foreach (var item in items)
        {
            double fraction = (double)item.Size / total;
            double segment = (horizontal ? w : h) * fraction;
            if (segment < MinRectSize)
            {
                continue;
            }

            var rect = horizontal
                ? new Windows.Foundation.Rect(x + offset, y, segment, h)
                : new Windows.Foundation.Rect(x, y + offset, w, segment);
            result.Add((item, rect));

            offset += segment;
        }

        return result;
    }
}
