#region MIT License

/*
 * MIT License
 *
 * Copyright (c) 2026 Peter Wagner
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

#endregion

namespace DiskEraser.Models;

/// <summary>
/// Represents a physical disk and its partitions for the layout diagram.
/// Populated from WMI Win32_DiskDrive and Win32_DiskPartition.
/// </summary>
public sealed class DiskInfo
{
    /// <summary>Physical disk index (0-based).</summary>
    public int DiskIndex { get; set; }

    /// <summary>Disk model/caption (e.g. "Samsung SSD 870").</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Total disk size in bytes.</summary>
    public ulong Size { get; set; }

    /// <summary>Disk status from WMI (e.g. "OK", "Pred Fail"); empty if unknown.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Partitions on this disk, in order.</summary>
    public List<PartitionInfo> Partitions { get; set; } = [];

    /// <summary>Short display caption (e.g. "Disk 0 - 500 GB").</summary>
    public string DisplayCaption => $"Disk {DiskIndex} - {FormattedSize}";

    /// <summary>Formatted size string.</summary>
    public string FormattedSize => FormatBytes(Size);

    private static string FormatBytes(ulong bytes) => ByteFormatter.Format(bytes);
}
