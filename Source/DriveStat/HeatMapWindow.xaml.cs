namespace DriveStat;

/// <summary>
/// Window that displays a 3D heat-map treemap: color intensity and depth represent size (cool = small, hot = large).
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
        WindowChrome.TryApplyMica(this);
        WindowChrome.TryExtendTitleBar(this, AppTitleBar);
        Title = $"{root.Name} — 3D Heat Map";
        TitleText.Text = Title;
        SizeChanged += (_, _) => Render();
        if (Content is FrameworkElement contentRoot)
        {
            contentRoot.Loaded += (_, _) => Render();
        }
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
        // Add extra space for 3D rotation effects
        HeatMapCanvas.Width = w + 100;
        HeatMapCanvas.Height = h + 100;

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
            
            var color = GetHeatColor(node.Size);

            // Calculate height based on size (normalized 0-1, then scaled)
            double heightNormalized = _maxSize > _minSize 
                ? (double)(node.Size - _minSize) / (_maxSize - _minSize) 
                : 0.5;
            double boxHeight = 15 + heightNormalized * 120; // Height ranges from 15 to 135 pixels

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

            // Draw right side face (vertical rectangle, right side)
            // Use Path to create a parallelogram for the right face
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
            if (baseWidth >= 40 && baseDepth >= 24)
            {
                var label = new TextBlock
                {
                    Text = node.Name,
                    FontSize = Math.Max(8, Math.Min(10, Math.Min(baseWidth, baseDepth) * 0.2)),
                    Foreground = new SolidColorBrush(Colors.White),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = baseWidth - 8
                };
                var labelBorder = new Border
                {
                    Child = label,
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

            ToolTipService.SetToolTip(container, $"{node.Name}\n{DirectoryNode.FormatSize(node.Size)}\nHeight: {boxHeight:F0}px");
            container.PointerPressed += (s, _) =>
            {
                if (s is FrameworkElement fe && fe.Tag is DirectoryNode n && n.IsDirectory)
                {
                    _root = n;
                    Title = $"{n.Name} — 3D Heat Map";
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
