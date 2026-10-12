namespace DiskEraser;

/// <summary>
/// Window for managing partitions: create, delete, format, assign letter, extend, shrink.
/// Uses diskpart for most operations; may require administrator rights.
/// </summary>
public sealed partial class PartitionManagementWindow : Window
{
    private IReadOnlyList<DiskInfo> _disks = [];
    private readonly ObservableCollection<PartitionListEntry> _partitionEntries = [];
    private readonly List<Microsoft.UI.Xaml.Shapes.Path> _pieSlicePaths = [];
    private HardwareEraseCapability? _hardwareEraseCapability;

    public PartitionManagementWindow()
    {
        InitializeComponent();
        App.ApplyTheme(this);
        WindowChrome.TryApplyMica(this);
        var titleBar = (Content as FrameworkElement)?.FindName("PartitionTitleBar") as UIElement;
        WindowChrome.TryExtendTitleBar(this, titleBar);
        WindowChrome.TrySetSize(this, 1245, 780);
        ClearHardwareEraseProbe();
        PartitionList.ItemsSource = _partitionEntries;
        App.DiagramColoursChanged += OnDiagramColoursChanged;
        Closed += (_, _) => App.DiagramColoursChanged -= OnDiagramColoursChanged;
        _ = LoadDisksAsync();
        WindowChrome.TryDisableMaximizeBox(this);
    }

    private void OnDiagramColoursChanged(object? sender, EventArgs e)
    {
        if (DiskCombo.SelectedItem is DiskInfo disk)
        {
            _ = BuildPieChartAsync(disk);
        }
    }

    /// <summary>Loads disk layout on a thread-pool thread, then updates UI on the dispatcher.</summary>
    private async Task LoadDisksAsync()
    {
        IReadOnlyList<DiskInfo> disks;
        try
        {
            disks = await DriveService.GetDiskPartitionLayoutAsync().ConfigureAwait(false);
        }
        catch
        {
            disks = [];
        }

        var queue = DispatcherQueue;
        if (queue == null)
        {
            return;
        }

        queue.TryEnqueue(() =>
        {
            _disks = disks;
            DiskCombo.Items.Clear();
            foreach (var d in _disks)
            {
                DiskCombo.Items.Add(d);
            }

            if (_disks.Count > 0)
            {
                DiskCombo.SelectedIndex = 0;
            }
            else
            {
                _partitionEntries.Clear();
            }
        });
    }

    private void RefreshDisks()
    {
        _disks = DriveService.GetDiskPartitionLayout();
        DiskCombo.Items.Clear();
        foreach (var d in _disks)
        {
            DiskCombo.Items.Add(d);
        }

        if (_disks.Count > 0)
        {
            DiskCombo.SelectedIndex = 0;
        }
        else
        {
            _partitionEntries.Clear();
        }
    }

    private void DiskCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _partitionEntries.Clear();
        ClearHardwareEraseProbe();
        if (DiskCombo.SelectedItem is not DiskInfo disk)
        {
            UpdateButtonStates(null);
            if (PieChartCanvas != null)
            {
                PieChartCanvas.Children.Clear();
            }
            _pieSlicePaths.Clear();
            return;
        }
        ulong used = 0;
        foreach (var p in disk.Partitions)
        {
            used += p.Size;
            _partitionEntries.Add(new PartitionListEntry
            {
                DisplayText = $"{p.DisplayLabel} — {p.FormattedSize} — {p.FileSystem}",
                IsUnallocated = false,
                Partition = p,
                DiskIndex = disk.DiskIndex
            });
        }
        ulong unallocated = disk.Size > used ? disk.Size - used : 0;
        if (unallocated > 0)
        {
            _partitionEntries.Add(new PartitionListEntry
            {
                DisplayText = $"Unallocated — {FormatBytes(unallocated)}",
                IsUnallocated = true,
                Unallocated = new UnallocatedInfo { DiskIndex = disk.DiskIndex, Size = unallocated },
                DiskIndex = disk.DiskIndex
            });
        }
        PartitionList.SelectedIndex = -1;
        UpdateButtonStates(null);
        _ = BuildPieChartAsync(disk);
    }

    /// <summary>Slice data computed on a background thread (no UI types). Label = short for on-slice; Tooltip = full for hover.</summary>
    private readonly record struct PieSliceData(double StartAngleDeg, double SweepDeg, Windows.UI.Color Color, string Label, string Tooltip);

    /// <summary>Computes pie slice angles, colors, and labels on a thread-pool thread; UI elements are created on the dispatcher.</summary>
    /// <param name="disk">The disk whose partitions are rendered in the pie chart.</param>
    /// <param name="selectedSliceIndex">When set, rotates the pie so this slice faces the user (bottom/front).</param>
    private async Task BuildPieChartAsync(DiskInfo disk, int? selectedSliceIndex = null)
    {
        if (PieChartCanvas == null)
        {
            return;
        }

        List<PieSliceData> slices = await Task.Run(() => ComputePieSliceData(disk)).ConfigureAwait(false);

        var queue = DispatcherQueue;
        if (queue == null)
        {
            return;
        }

        queue.TryEnqueue(() => CreatePieChartFromData(slices, selectedSliceIndex));
    }

    /// <summary>Runs on any thread; returns slice parameters and labels (no UI objects).</summary>
    private static List<PieSliceData> ComputePieSliceData(DiskInfo disk)
    {
        ulong total = disk.Size > 0 ? disk.Size : 1;
        double startAngleDeg = 0;
        var slices = new List<PieSliceData>();

        Windows.UI.Color formattable = AppSettings.ParseColor(AppSettings.GetFormattableColour()) ?? Colors.SteelBlue;
        Windows.UI.Color systemDrive = AppSettings.ParseColor(AppSettings.GetSystemDriveColour()) ?? Colors.OrangeRed;
        Windows.UI.Color noLetter = AppSettings.ParseColor(AppSettings.GetNoLetterColour()) ?? Colors.Gray;
        Windows.UI.Color unallocated = AppSettings.ParseColor(AppSettings.GetUnallocatedColour()) ?? Colors.DarkGray;

        foreach (var p in disk.Partitions)
        {
            double sweepDeg = 360.0 * (p.Size / (double)total);
            if (sweepDeg <= 0)
            {
                continue;
            }

            Windows.UI.Color fill = p.IsProtected ? systemDrive : string.IsNullOrEmpty(p.DriveLetter) ? noLetter : formattable;
            string label = $"{p.DisplayLabel} — {p.FormattedSize} — {p.FileSystem}";
            string tooltip = p.GetTooltipText(disk.Status);
            slices.Add(new PieSliceData(startAngleDeg, sweepDeg, fill, label, tooltip));
            startAngleDeg += sweepDeg;
        }

        ulong used = disk.Partitions.Aggregate(0UL, (s, p) => s + p.Size);
        ulong free = disk.Size > used ? disk.Size - used : 0;
        if (free > 0)
        {
            double sweepDeg = 360.0 * (free / (double)total);
            string label = "Unallocated — " + FormatBytes(free);
            string tooltip = string.IsNullOrWhiteSpace(disk.Status) ? label : $"{label}\nHealth: {disk.Status.Trim()}";
            slices.Add(new PieSliceData(startAngleDeg, sweepDeg, unallocated, label, tooltip));
        }

        return slices;
    }

    /// <summary>Runs on UI thread; creates chart with labels on each slice.</summary>
    /// <param name="slices">Precomputed pie slice data from ComputePieSliceData.</param>
    /// <param name="selectedSliceIndex">When valid, rotates the pie so this slice faces the user (bottom/front at 90°).</param>
    private void CreatePieChartFromData(List<PieSliceData> slices, int? selectedSliceIndex = null)
    {
        if (PieChartCanvas == null)
        {
            return;
        }

        PieChartCanvas.Children.Clear();
        _pieSlicePaths.Clear();

        // Canvas is 520×300; center ellipse with 3D tilt (ry < rx).
        const double cx = 260, cy = 150, rx = 240, ry = 140;
        const double depth = 18; // Vertical offset for 3D thickness

        // Rotation: bring selected slice center to 90° (bottom/front of 3D pie).
        double offset = 0;
        if (selectedSliceIndex is >= 0 && selectedSliceIndex.Value < slices.Count)
        {
            var sel = slices[selectedSliceIndex.Value];
            double centerAngle = sel.StartAngleDeg + sel.SweepDeg * 0.5;
            offset = 90.0 - centerAngle;
        }

        // 1. Base ellipse (bottom of pie) – dark, offset down for depth.
        AddPieBaseToCanvas(PieChartCanvas, cx, cy + depth, rx, ry);

        // 2. Back rim – rear half of ellipse, rotated with the pie.
        AddPieBackRimToCanvas(PieChartCanvas, cx, cy, rx, ry, offset);

        // 3. Per-slice side faces (darker shade for 3D depth).
        for (int i = 0; i < slices.Count; i++)
        {
            var slice = slices[i];
            double start = NormalizeAngle(slice.StartAngleDeg + offset);
            AddPieSliceSideToCanvas(PieChartCanvas, cx, cy, rx, ry, depth, start, slice.SweepDeg, slice.Color);
        }

        // 4. Front slices (top of pie) with white separators. Draw slices first, then labels on top (ZIndex 2).
        bool hasSelection = selectedSliceIndex is >= 0 && selectedSliceIndex.Value < slices.Count;
        for (int i = 0; i < slices.Count; i++)
        {
            var slice = slices[i];
            double start = NormalizeAngle(slice.StartAngleDeg + offset);
            var path = AddPieSliceToCanvas(PieChartCanvas, cx, cy, rx, ry, start, slice.SweepDeg, slice.Color, slice.Tooltip);
            _pieSlicePaths.Add(path);

            int index = i;
            path.PointerEntered += (_, _) => HighlightSlice(index, true);
            path.PointerExited += (_, _) => HighlightSlice(index, false);
            path.Tapped += (_, _) =>
            {
                if (PartitionList != null && index >= 0 && index < _partitionEntries.Count)
                {
                    PartitionList.SelectedIndex = index;
                }
            };

            if (hasSelection && i == selectedSliceIndex!.Value)
            {
                path.RenderTransform = new TransformGroup
                {
                    Children =
                    {
                        new ScaleTransform { CenterX = cx, CenterY = cy, ScaleX = 1.08, ScaleY = 1.08 },
                        new TranslateTransform { Y = 6 }
                    }
                };
                Canvas.SetZIndex(path, 1);
            }
            else
            {
                Canvas.SetZIndex(path, 0);
            }
        }
        foreach (var slice in slices)
        {
            double start = NormalizeAngle(slice.StartAngleDeg + offset);
            if (slice.SweepDeg >= 20.0)
            {
                AddSliceLabelToCanvas(PieChartCanvas, cx, cy, rx, ry, start, slice.SweepDeg, slice.Label);
            }
        }
    }

    private static double NormalizeAngle(double deg)
    {
        double d = deg % 360.0;
        return d < 0 ? d + 360.0 : d;
    }

    private void HighlightSlice(int index, bool highlight)
    {
        if (index < 0 || index >= _pieSlicePaths.Count)
        {
            return;
        }

        var path = _pieSlicePaths[index];
        path.StrokeThickness = highlight ? 2.5 : 1;
        path.Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
    }

    private void PartitionRow_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PartitionListEntry entry)
        {
            return;
        }

        int index = _partitionEntries.IndexOf(entry);
        HighlightSlice(index, true);
    }

    private void PartitionRow_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PartitionListEntry entry)
        {
            return;
        }

        int index = _partitionEntries.IndexOf(entry);
        HighlightSlice(index, false);
    }

    /// <summary>Adds a label on the pie slice at its middle angle (on the UI thread).</summary>
    private static void AddSliceLabelToCanvas(Canvas canvas, double cx, double cy, double rx, double ry, double startAngleDeg, double sweepDeg, string label)
    {
        double midAngleDeg = startAngleDeg + sweepDeg * 0.5;
        double midRad = midAngleDeg * Math.PI / 180.0;
        // Position at 55% of radius so text sits on the slice.
        double labelDistX = 0.55 * rx;
        double labelDistY = 0.55 * ry;
        double x = cx + labelDistX * Math.Cos(midRad);
        double y = cy + labelDistY * Math.Sin(midRad);

        // Shorten label for on-chart display.
        string displayText = label.Length > 22 ? label[..19] + "…" : label;

        var tb = new TextBlock
        {
            Text = displayText,
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            TextAlignment = TextAlignment.Center,
            MaxWidth = 90,
            TextWrapping = TextWrapping.Wrap,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(tb, x - 45);
        Canvas.SetTop(tb, y - 10);
        Canvas.SetZIndex(tb, 2);
        canvas.Children.Add(tb);
    }

    /// <summary>Draws the curved side face of a slice (connects top arc to base) for 3D depth. Uses a darker shade of the slice color.</summary>
    private static void AddPieSliceSideToCanvas(Canvas canvas, double cx, double cy, double rx, double ry, double depth, double startAngleDeg, double sweepDeg, Windows.UI.Color color)
    {
        if (sweepDeg <= 0)
        {
            return;
        }

        double startRad = startAngleDeg * Math.PI / 180.0;
        double endRad = (startAngleDeg + sweepDeg) * Math.PI / 180.0;
        double x1 = cx + rx * Math.Cos(startRad);
        double y1 = cy + ry * Math.Sin(startRad);
        double x2 = cx + rx * Math.Cos(endRad);
        double y2 = cy + ry * Math.Sin(endRad);
        double bx1 = cx + rx * Math.Cos(startRad);
        double by1 = cy + depth + ry * Math.Sin(startRad);
        double bx2 = cx + rx * Math.Cos(endRad);
        double by2 = cy + depth + ry * Math.Sin(endRad);

        var sideColor = Windows.UI.Color.FromArgb(255,
            (byte)(color.R * 0.55),
            (byte)(color.G * 0.55),
            (byte)(color.B * 0.55));

        var figure = new PathFigure
        {
            StartPoint = new Windows.Foundation.Point(x1, y1),
            IsClosed = true
        };
        figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(bx1, by1) });
        figure.Segments.Add(new ArcSegment
        {
            Point = new Windows.Foundation.Point(bx2, by2),
            Size = new Windows.Foundation.Size(rx, ry),
            IsLargeArc = sweepDeg > 180,
            SweepDirection = SweepDirection.Clockwise
        });
        figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(x2, y2) });
        figure.Segments.Add(new ArcSegment
        {
            Point = new Windows.Foundation.Point(x1, y1),
            Size = new Windows.Foundation.Size(rx, ry),
            IsLargeArc = sweepDeg > 180,
            SweepDirection = SweepDirection.Counterclockwise
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        var path = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geometry,
            Fill = new SolidColorBrush(sideColor),
            Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 0, 0, 0)),
            StrokeThickness = 0.5
        };
        Canvas.SetLeft(path, 0);
        Canvas.SetTop(path, 0);
        canvas.Children.Add(path);
    }

    /// <summary>Draws the base ellipse (bottom of 3D pie) in a dark shade.</summary>
    private static void AddPieBaseToCanvas(Canvas canvas, double cx, double cy, double rx, double ry)
    {
        var baseColor = Windows.UI.Color.FromArgb(255, 45, 45, 52);
        var ellipse = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = rx * 2,
            Height = ry * 2,
            Fill = new SolidColorBrush(baseColor),
            Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(120, 0, 0, 0)),
            StrokeThickness = 1
        };
        Canvas.SetLeft(ellipse, cx - rx);
        Canvas.SetTop(ellipse, cy - ry);
        canvas.Children.Add(ellipse);
    }

    /// <summary>Draws the back rim (rear half arc) so the top slices appear in front. Rotates with the pie when offset is non-zero.</summary>
    private static void AddPieBackRimToCanvas(Canvas canvas, double cx, double cy, double rx, double ry, double angleOffsetDeg = 0)
    {
        // Back half: from 180° to 360°, rotated by offset.
        double startAngleDeg = 180.0 + angleOffsetDeg;
        const double sweepDeg = 180.0;
        startAngleDeg = startAngleDeg % 360.0;
        if (startAngleDeg < 0)
        {
            startAngleDeg += 360.0;
        }

        double startRad = startAngleDeg * Math.PI / 180.0;
        double endRad = (startAngleDeg + sweepDeg) * Math.PI / 180.0;
        double x1 = cx + rx * Math.Cos(startRad);
        double y1 = cy + ry * Math.Sin(startRad);
        double x2 = cx + rx * Math.Cos(endRad);
        double y2 = cy + ry * Math.Sin(endRad);

        var figure = new PathFigure
        {
            StartPoint = new Windows.Foundation.Point(cx, cy),
            IsClosed = true
        };
        figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(x1, y1) });
        figure.Segments.Add(new ArcSegment
        {
            Point = new Windows.Foundation.Point(x2, y2),
            Size = new Windows.Foundation.Size(rx, ry),
            IsLargeArc = sweepDeg > 180,
            SweepDirection = SweepDirection.Clockwise
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        var path = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geometry,
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 55, 55, 62)),
            Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(150, 0, 0, 0)),
            StrokeThickness = 1
        };
        Canvas.SetLeft(path, 0);
        Canvas.SetTop(path, 0);
        canvas.Children.Add(path);
    }

    /// <summary>Creates tooltip content with an Acrylic (Mica-style) frosted background for use in popups.</summary>
    private static FrameworkElement CreateMicaStyleToolTipContent(string text)
    {
        var brush = new AcrylicBrush
        {
            TintColor = Windows.UI.Color.FromArgb(255, 32, 32, 32),
            TintOpacity = 0.9,
            FallbackColor = Windows.UI.Color.FromArgb(230, 32, 32, 32)
        };
        return new Border
        {
            Background = brush,
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(4),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 320,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                FontSize = 12
            }
        };
    }

    private static Microsoft.UI.Xaml.Shapes.Path AddPieSliceToCanvas(Canvas canvas, double cx, double cy, double rx, double ry, double startAngleDeg, double sweepDeg, Windows.UI.Color color, string? tooltip = null)
    {
        Microsoft.UI.Xaml.Shapes.Path pathShape;

        // ArcSegment with 360° has same start/end point and doesn't render; draw a full ellipse instead.
        if (sweepDeg >= 359.0)
        {
            pathShape = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = new EllipseGeometry
                {
                    Center = new Windows.Foundation.Point(cx, cy),
                    RadiusX = rx,
                    RadiusY = ry
                },
                Fill = new SolidColorBrush(color),
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                StrokeThickness = 1
            };
        }
        else
        {
            double startRad = startAngleDeg * Math.PI / 180.0;
            double endRad = (startAngleDeg + sweepDeg) * Math.PI / 180.0;
            double x1 = cx + rx * Math.Cos(startRad);
            double y1 = cy + ry * Math.Sin(startRad);
            double x2 = cx + rx * Math.Cos(endRad);
            double y2 = cy + ry * Math.Sin(endRad);

            var figure = new PathFigure
            {
                StartPoint = new Windows.Foundation.Point(cx, cy),
                IsClosed = true
            };
            figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(x1, y1) });
            figure.Segments.Add(new ArcSegment
            {
                Point = new Windows.Foundation.Point(x2, y2),
                Size = new Windows.Foundation.Size(rx, ry),
                IsLargeArc = sweepDeg > 180,
                SweepDirection = SweepDirection.Clockwise
            });
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            pathShape = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = geometry,
                Fill = new SolidColorBrush(color),
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                StrokeThickness = 1
            };
        }

        if (!string.IsNullOrEmpty(tooltip))
        {
            ToolTipService.SetToolTip(pathShape, CreateMicaStyleToolTipContent(tooltip));
        }

        Canvas.SetLeft(pathShape, 0);
        Canvas.SetTop(pathShape, 0);
        canvas.Children.Add(pathShape);
        return pathShape;
    }

    /// <summary>Shows a confirmation dialog before formatting. User must check a box to proceed.</summary>
    private Task<bool> ShowFormatConfirmDialogAsync(string driveLetter)
        => DestructiveConfirm.ConfirmWithAcknowledgementAsync(
            Content.XamlRoot,
            "Confirm format",
            DestructiveConfirm.FormatVolumeMessage(driveLetter, "NTFS", freeSpaceOverwrite: false, wipePasses: 0),
            "Format",
            "I understand that all data will be permanently erased and I want to proceed");

    private static string FormatBytes(ulong bytes) => ByteFormatter.Format(bytes);

    private void PartitionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateButtonStates(PartitionList.SelectedItem as PartitionListEntry);
        // Rotate pie so selected slice faces the user.
        if (DiskCombo.SelectedItem is DiskInfo disk)
        {
            int idx = PartitionList.SelectedIndex;
            _ = BuildPieChartAsync(disk, idx >= 0 ? idx : null);
        }
    }

    private void UpdateButtonStates(PartitionListEntry? entry)
    {
        bool unallocated = entry?.IsUnallocated == true;
        bool partition = entry?.Partition != null;
        var part = entry?.Partition;
        bool hasLetter = !string.IsNullOrEmpty(part?.DriveLetter);
        bool isProtected = part?.IsProtected == true;

        CreatePartitionButton.IsEnabled = unallocated;
        DeletePartitionButton.IsEnabled = partition && !isProtected;
        FormatButton.IsEnabled = partition && hasLetter && !isProtected;
        AssignLetterButton.IsEnabled = partition && !isProtected;
        ExtendButton.IsEnabled = partition && hasLetter && !isProtected;
        ShrinkButton.IsEnabled = partition && hasLetter && !isProtected;
    }

    private void ShowStatus(string text, bool isError = false)
    {
        if (StatusText == null)
        {
            return;
        }

        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
    }

    private void HideStatus()
    {
        if (StatusText != null)
        {
            StatusText.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Shows success or the last diskpart output when an operation fails.</summary>
    private void ShowDiskPartOutcome(bool ok, string successMessage)
    {
        if (ok)
        {
            ShowStatus(successMessage);
            return;
        }

        string detail = DriveService.LastDiskPartOutput;
        if (string.IsNullOrWhiteSpace(detail))
        {
            ShowStatus("Operation failed. Try running as administrator. See AppLog for details.", true);
            return;
        }

        ShowStatus($"Failed: {TruncateForUi(detail)}", true);
    }

    private static string TruncateForUi(string text, int max = 280)
        => text.Length <= max ? text : text[..max] + "…";

    /// <summary>Bytes of free space immediately after <paramref name="part"/> on <paramref name="disk"/>.</summary>
    private static ulong GetContiguousUnallocatedAfter(DiskInfo disk, PartitionInfo part)
    {
        ulong end = part.Offset + part.Size;
        ulong nextStart = disk.Size;
        foreach (var p in disk.Partitions)
        {
            if (p.Offset >= end && p.Offset < nextStart)
            {
                nextStart = p.Offset;
            }
        }

        return nextStart > end ? nextStart - end : 0;
    }

    private async Task<bool> ConfirmSizeChangePreviewAsync(string title, string before, string after, string note)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = $"Before: {before}", TextWrapping = TextWrapping.WrapWholeWords });
        panel.Children.Add(new TextBlock { Text = $"After:  {after}", TextWrapping = TextWrapping.WrapWholeWords });
        if (!string.IsNullOrWhiteSpace(note))
        {
            panel.Children.Add(new TextBlock
            {
                Text = note,
                TextWrapping = TextWrapping.WrapWholeWords,
                Opacity = 0.8
            });
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "Apply",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void CreatePartitionButton_Click(object sender, RoutedEventArgs e)
    {
        var entry = PartitionList.SelectedItem as PartitionListEntry;
        if (entry?.IsUnallocated != true || entry.Unallocated == null)
        {
            ShowStatus("Select unallocated space first.", true);
            return;
        }
        var panel = new StackPanel { Spacing = 8 };
        var sizeBox = new TextBox { PlaceholderText = "0 = all space", Width = 200, InputScope = new InputScope() };
        sizeBox.InputScope.Names.Add(new InputScopeName(InputScopeNameValue.Number));
        panel.Children.Add(new TextBlock { Text = "Size in MB (0 = use all unallocated):" });
        panel.Children.Add(sizeBox);
        var dialog = new ContentDialog
        {
            Title = "Create partition",
            Content = panel,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        uint sizeMb = 0;
        if (!string.IsNullOrWhiteSpace(sizeBox.Text) && !uint.TryParse(sizeBox.Text.Trim(), out sizeMb))
        {
            sizeMb = 0;
        }

        HideStatus();
        bool ok = DriveService.CreatePartition(entry.DiskIndex, sizeMb);
        if (ok)
        {
            ShowStatus("Partition created. Refreshing.");
            _ = LoadDisksAsync();
        }
        else
        {
            ShowDiskPartOutcome(false, string.Empty);
        }
    }

    private async void DeletePartitionButton_Click(object sender, RoutedEventArgs e)
    {
        var entry = PartitionList.SelectedItem as PartitionListEntry;
        if (entry?.Partition == null || entry.Partition.IsProtected)
        {
            ShowStatus("Select a non-system partition to delete.", true);
            return;
        }
        if (!await DestructiveConfirm.ConfirmWithAcknowledgementAsync(
                Content.XamlRoot,
                "Delete partition",
                DestructiveConfirm.DeletePartitionMessage(entry.Partition.DisplayLabel),
                "Delete"))
        {
            return;
        }

        HideStatus();
        bool ok = DriveService.DeletePartition(entry.DiskIndex, entry.Partition.PartitionIndex);
        if (ok)
        {
            ShowStatus("Partition deleted. Refreshing.");
            _ = LoadDisksAsync();
        }
        else
        {
            ShowDiskPartOutcome(false, string.Empty);
        }
    }

    private async void FormatButton_Click(object sender, RoutedEventArgs e)
    {
        var entry = PartitionList.SelectedItem as PartitionListEntry;
        if (entry?.Partition == null || string.IsNullOrEmpty(entry.Partition.DriveLetter) || entry.Partition.IsProtected)
        {
            return;
        }

        bool confirmed = await ShowFormatConfirmDialogAsync(entry.Partition.DriveLetter);
        if (!confirmed)
        {
            return;
        }

        HideStatus();
        uint result = await DriveService.FormatAsync(entry.Partition.DriveLetter, "NTFS", true, 0, "", false);
        string msg = DriveService.GetFormatResultMessage(result);
        if (result == 0)
        {
            ShowStatus("Format completed.");
            _ = LoadDisksAsync();
        }
        else
        {
            ShowStatus(msg, true);
        }
    }

    private async void AssignLetterButton_Click(object sender, RoutedEventArgs e)
    {
        var entry = PartitionList.SelectedItem as PartitionListEntry;
        if (entry?.Partition == null || entry.Partition.IsProtected)
        {
            return;
        }

        var part = entry.Partition;
        string currentLetter = part.DriveLetter.TrimEnd(':');
        var usedLetters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var disk in _disks)
        {
            foreach (var p in disk.Partitions)
            {
                if (!string.IsNullOrEmpty(p.DriveLetter))
                {
                    usedLetters.Add(p.DriveLetter.TrimEnd(':'));
                }
            }
        }

        var available = new List<string>();
        for (char c = 'A'; c <= 'Z'; c++)
        {
            string letter = c.ToString();
            if (!usedLetters.Contains(letter))
            {
                available.Add(letter + ":");
            }
        }
        if (available.Count == 0)
        {
            ShowStatus("No free drive letters.", true);
            return;
        }
        var combo = new ComboBox { ItemsSource = available, SelectedIndex = 0, Width = 120 };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(part.DriveLetter) ? "Assign drive letter:" : "Change drive letter:" });
        panel.Children.Add(combo);
        var dialog = new ContentDialog
        {
            Title = string.IsNullOrEmpty(part.DriveLetter) ? "Assign letter" : "Change letter",
            Content = panel,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        string newLetter = (combo.SelectedItem as string) ?? available[0];
        newLetter = newLetter.TrimEnd(':');
        var ok = string.IsNullOrEmpty(part.DriveLetter)
            ? DriveService.AssignLetterToPartition(entry.DiskIndex, part.PartitionIndex, newLetter)
            : DriveService.TryAssignDriveLetter(currentLetter, newLetter);

        HideStatus();
        if (ok)
        {
            ShowStatus($"Drive letter set to {newLetter}:. Refreshing.");
            _ = LoadDisksAsync();
        }
        else
        {
            ShowDiskPartOutcome(false, string.Empty);
        }
    }

    private async void ExtendButton_Click(object sender, RoutedEventArgs e)
    {
        var entry = PartitionList.SelectedItem as PartitionListEntry;
        if (entry?.Partition == null || string.IsNullOrEmpty(entry.Partition.DriveLetter) || entry.Partition.IsProtected)
        {
            return;
        }

        int? volNum = DriveService.GetVolumeNumberForDriveLetter(entry.Partition.DriveLetter);
        if (volNum == null)
        {
            ShowStatus("Could not get volume. Run the app as administrator for Extend/Shrink.", true);
            return;
        }
        var sizeBox = new TextBox { PlaceholderText = "0 = use all", Width = 200 };
        sizeBox.InputScope = new InputScope();
        sizeBox.InputScope.Names.Add(new InputScopeName(InputScopeNameValue.Number));
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Additional size in MB (0 = use all contiguous unallocated):" });
        panel.Children.Add(sizeBox);
        var dialog = new ContentDialog
        {
            Title = "Extend volume",
            Content = panel,
            PrimaryButtonText = "Next",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        uint? sizeMb = null;
        if (!string.IsNullOrWhiteSpace(sizeBox.Text) && uint.TryParse(sizeBox.Text.Trim(), out uint s))
        {
            sizeMb = s == 0 ? null : s;
        }

        ulong before = entry.Partition.Size;
        ulong addBytes;
        string note;
        if (DiskCombo.SelectedItem is DiskInfo disk)
        {
            ulong contiguous = GetContiguousUnallocatedAfter(disk, entry.Partition);
            if (sizeMb is null or 0)
            {
                addBytes = contiguous;
                note = contiguous == 0
                    ? "No contiguous unallocated space was found after this partition. diskpart may still fail."
                    : $"Uses all contiguous unallocated space after this partition ({FormatBytes(contiguous)}).";
            }
            else
            {
                addBytes = sizeMb.Value * 1024UL * 1024UL;
                note = addBytes > contiguous && contiguous > 0
                    ? $"Requested growth exceeds contiguous free space ({FormatBytes(contiguous)}). diskpart may fail."
                    : "Extends into contiguous unallocated space after this partition.";
            }
        }
        else
        {
            addBytes = (sizeMb ?? 0) * 1024UL * 1024UL;
            note = "Preview uses the size you entered; actual growth depends on diskpart.";
        }

        ulong after = before + addBytes;
        if (!await ConfirmSizeChangePreviewAsync(
                "Preview extend",
                $"{entry.Partition.DisplayLabel} — {FormatBytes(before)}",
                $"{entry.Partition.DisplayLabel} — {FormatBytes(after)}",
                note))
        {
            return;
        }

        HideStatus();
        bool ok = DriveService.ExtendVolume(volNum.Value, sizeMb);
        if (ok)
        {
            ShowStatus("Volume extended. Refreshing.");
            _ = LoadDisksAsync();
        }
        else
        {
            ShowDiskPartOutcome(false, string.Empty);
        }
    }

    private async void ShrinkButton_Click(object sender, RoutedEventArgs e)
    {
        var entry = PartitionList.SelectedItem as PartitionListEntry;
        if (entry?.Partition == null || string.IsNullOrEmpty(entry.Partition.DriveLetter) || entry.Partition.IsProtected)
        {
            return;
        }

        int? volNum = DriveService.GetVolumeNumberForDriveLetter(entry.Partition.DriveLetter);
        if (volNum == null)
        {
            ShowStatus("Could not get volume. Run the app as administrator for Extend/Shrink.", true);
            return;
        }
        var sizeBox = new TextBox { PlaceholderText = "MB to shrink by", Width = 200 };
        sizeBox.InputScope = new InputScope();
        sizeBox.InputScope.Names.Add(new InputScopeName(InputScopeNameValue.Number));
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Amount to shrink (MB):" });
        panel.Children.Add(sizeBox);
        var dialog = new ContentDialog
        {
            Title = "Shrink volume",
            Content = panel,
            PrimaryButtonText = "Next",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (!uint.TryParse(sizeBox.Text?.Trim(), out uint desiredMB) || desiredMB == 0)
        {
            ShowStatus("Enter a valid size in MB.", true);
            return;
        }

        ulong before = entry.Partition.Size;
        ulong shrinkBytes = desiredMB * 1024UL * 1024UL;
        if (shrinkBytes >= before)
        {
            ShowStatus("Shrink amount must be smaller than the current partition size.", true);
            return;
        }

        ulong after = before - shrinkBytes;
        if (!await ConfirmSizeChangePreviewAsync(
                "Preview shrink",
                $"{entry.Partition.DisplayLabel} — {FormatBytes(before)}",
                $"{entry.Partition.DisplayLabel} — {FormatBytes(after)}",
                "Creates unallocated space after the volume. Actual shrink may be limited by immovable files."))
        {
            return;
        }

        HideStatus();
        bool ok = DriveService.ShrinkVolume(volNum.Value, desiredMB);
        if (ok)
        {
            ShowStatus("Volume shrunk. Refreshing.");
            _ = LoadDisksAsync();
        }
        else
        {
            ShowDiskPartOutcome(false, string.Empty);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        HideStatus();
        await LoadDisksAsync();
        if (DiskCombo.SelectedItem is DiskInfo d)
        {
            DiskCombo_SelectionChanged(DiskCombo, new SelectionChangedEventArgs(null, null));
        }
    }

    private void ClearHardwareEraseProbe()
    {
        _hardwareEraseCapability = null;
        if (HardwareEraseCapabilityText != null)
        {
            HardwareEraseCapabilityText.Text = "Select a disk and probe capabilities (admin recommended).";
        }

        if (HardwareEraseButton != null)
        {
            HardwareEraseButton.IsEnabled = false;
        }
    }

    private void ProbeHardwareEraseButton_Click(object sender, RoutedEventArgs e)
    {
        if (DiskCombo.SelectedItem is not DiskInfo disk)
        {
            ShowStatus("Select a disk first.", true);
            return;
        }

        HideStatus();
        try
        {
            _hardwareEraseCapability = HardwareEraseService.QueryCapabilities(disk.DiskIndex);
            bool protectedDisk = disk.Partitions.Any(p => p.IsProtected);
            HardwareEraseCapabilityText.Text =
                $"Disk {disk.DiskIndex} ({_hardwareEraseCapability.Model})\n{_hardwareEraseCapability.Summary}"
                + (protectedDisk ? "\nRefused: disk contains a protected volume." : string.Empty)
                + (!_hardwareEraseCapability.CanAttemptHardwareErase
                    ? "\nHardware erase button stays disabled until a usable method is available."
                    : string.Empty);

            HardwareEraseButton.IsEnabled =
                _hardwareEraseCapability.CanAttemptHardwareErase
                && !protectedDisk
                && ElevationHelper.IsElevated;
        }
        catch (Exception ex)
        {
            AppLog.Error("Hardware erase probe failed.", ex, nameof(PartitionManagementWindow));
            ShowStatus($"Probe failed: {ex.Message}", true);
            ClearHardwareEraseProbe();
        }
    }

    private async void HardwareEraseButton_Click(object sender, RoutedEventArgs e)
    {
        if (DiskCombo.SelectedItem is not DiskInfo disk || _hardwareEraseCapability == null)
        {
            ShowStatus("Probe capabilities first.", true);
            return;
        }

        if (HardwareEraseAdvancedCheck?.IsChecked != true)
        {
            ShowStatus("Check the advanced acknowledgement box first.", true);
            return;
        }

        if (!ElevationHelper.IsElevated)
        {
            ShowStatus("Run as Administrator for hardware erase.", true);
            return;
        }

        if (disk.Partitions.Any(p => p.IsProtected) || !_hardwareEraseCapability.CanAttemptHardwareErase)
        {
            ShowStatus("Hardware erase is not allowed for this disk.", true);
            return;
        }

        string eraseWord = $"ERASE-{disk.DiskIndex}";
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text =
                $"This issues a controller hardware erase (NVMe Sanitize when available) for the ENTIRE Disk {disk.DiskIndex} ({disk.Model}).\n\n"
                + $"{_hardwareEraseCapability.Summary}\n\n"
                + "Not a certified wipe. Remapped cells may retain data. Type the confirmation phrase exactly:",
            TextWrapping = TextWrapping.WrapWholeWords
        });
        var confirmBox = new TextBox { PlaceholderText = eraseWord, Width = 280 };
        panel.Children.Add(confirmBox);

        if (!await DestructiveConfirm.ConfirmWithAcknowledgementAsync(
                Content.XamlRoot,
                "Hardware erase — entire disk",
                $"You are about to destroy all data on Disk {disk.DiskIndex}. This cannot be undone.",
                "Continue"))
        {
            return;
        }

        var typeDialog = new ContentDialog
        {
            Title = "Type confirmation phrase",
            Content = panel,
            PrimaryButtonText = "Erase disk",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        if (await typeDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (!string.Equals(confirmBox.Text?.Trim(), eraseWord, StringComparison.Ordinal))
        {
            ShowStatus($"Confirmation phrase must be exactly: {eraseWord}", true);
            return;
        }

        HideStatus();
        ShowStatus("Submitting hardware erase…");
        var (ok, message) = await HardwareEraseService.TryHardwareEraseAsync(disk.DiskIndex, _hardwareEraseCapability);
        ShowStatus(message, !ok);
        if (ok)
        {
            _ = LoadDisksAsync();
        }
    }
}
