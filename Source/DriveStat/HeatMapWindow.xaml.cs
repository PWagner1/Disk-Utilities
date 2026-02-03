namespace DriveStat;

/// <summary>
/// Window that displays a heat-map treemap: color intensity represents size (cool = small, hot = large).
/// </summary>
public sealed partial class HeatMapWindow : Window
{
    private DirectoryNode? _root;
    private long _minSize;
    private long _maxSize;
    private const double MinRectSize = 4;
    private const double RectPadding = 1;

    // Heat gradient: blue (cool) -> cyan -> green -> yellow -> red (hot)
    private static readonly Windows.UI.Color[] HeatColors =
    [
        Windows.UI.Color.FromArgb(255, 32, 98, 168),   // blue
        Windows.UI.Color.FromArgb(255, 58, 157, 35),  // green
        Windows.UI.Color.FromArgb(255, 230, 194, 41),  // yellow
        Windows.UI.Color.FromArgb(255, 192, 57, 43),   // red
    ];

    public HeatMapWindow(DirectoryNode root)
    {
        InitializeComponent();
        _root = root;
        TrySetMicaBackdrop();
        TrySetExtendedTitleBar();
        Title = $"{root.Name} — Heat Map";
        TitleText.Text = Title;
        SizeChanged += (_, _) => Render();
        if (Content is FrameworkElement contentRoot)
        {
            contentRoot.Loaded += (_, _) => Render();
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
        catch { }
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

    private void HeatMapScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        // Optional: update zoom label if we add one
    }

    private void Render()
    {
        HeatMapCanvas.Children.Clear();
        if (_root == null || _root.Size <= 0)
        {
            return;
        }

        double w = HeatMapScrollViewer.ActualWidth > 0 ? HeatMapScrollViewer.ActualWidth - 48 : 800;
        double h = HeatMapScrollViewer.ActualHeight > 0 ? HeatMapScrollViewer.ActualHeight - 48 : 600;
        w = Math.Max(400, w);
        h = Math.Max(300, h);
        HeatMapCanvas.Width = w;
        HeatMapCanvas.Height = h;

        var items = _root.Children;
        if (items.Count == 0)
        {
            return;
        }

        _minSize = items.Min(i => i.Size);
        _maxSize = items.Max(i => i.Size);
        if (_maxSize <= _minSize)
        {
            _maxSize = _minSize + 1;
        }

        var rects = Squarify(items, 0, 0, w, h);
        foreach (var (node, rect) in rects)
        {
            var blockWidth = Math.Max(0, rect.Width - RectPadding * 2);
            var blockHeight = Math.Max(0, rect.Height - RectPadding * 2);
            var color = GetHeatColor(node.Size);
            var brush = new SolidColorBrush(color);
            var border = new SolidColorBrush(Colors.Black) { Opacity = 0.15 };

            var container = new Grid
            {
                Width = blockWidth,
                Height = blockHeight,
                Tag = node,
                Background = brush
            };

            var rectShape = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = blockWidth,
                Height = blockHeight,
                Fill = brush,
                Stroke = border,
                StrokeThickness = 0.5
            };
            container.Children.Add(rectShape);

            if (blockWidth >= 40 && blockHeight >= 24)
            {
                var label = new TextBlock
                {
                    Text = node.Name,
                    FontSize = Math.Max(9, Math.Min(11, blockHeight * 0.3)),
                    Foreground = new SolidColorBrush(Colors.White),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(4, 0, 4, 0)
                };
                var labelBorder = new Border
                {
                    Child = label,
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(100, 0, 0, 0)),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(4, 2, 4, 2),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                container.Children.Add(labelBorder);
            }

            Canvas.SetLeft(container, rect.X + RectPadding);
            Canvas.SetTop(container, rect.Y + RectPadding);

            ToolTipService.SetToolTip(container, $"{node.Name}\n{DirectoryNode.FormatSize(node.Size)}");
            container.PointerPressed += (s, _) =>
            {
                if (s is FrameworkElement fe && fe.Tag is DirectoryNode n && n.IsDirectory)
                {
                    _root = n;
                    Title = $"{n.Name} — Heat Map";
                    TitleText.Text = Title;
                    Render();
                }
            };

            HeatMapCanvas.Children.Add(container);
        }
    }

    private Windows.UI.Color GetHeatColor(long size)
    {
        if (_maxSize <= _minSize)
        {
            return HeatColors[0];
        }
        double t = (double)(size - _minSize) / (_maxSize - _minSize);
        t = Math.Clamp(t, 0, 1);
        double segment = 1.0 / (HeatColors.Length - 1);
        int i = (int)Math.Min(Math.Floor(t / segment), HeatColors.Length - 2);
        double local = (t - i * segment) / segment;
        var a = HeatColors[i];
        var b = HeatColors[i + 1];
        return Windows.UI.Color.FromArgb(255,
            (byte)(a.R + (b.R - a.R) * local),
            (byte)(a.G + (b.G - a.G) * local),
            (byte)(a.B + (b.B - a.B) * local));
    }

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
