namespace DiskEraser.Models;

/// <summary>
/// Represents a logical drive/volume for display and formatting operations.
/// Data is populated from WMI Win32_Volume and used by the UI.
/// </summary>
public sealed class DriveItem
{
    /// <summary>Gets or sets the drive letter and colon (e.g. "D:").</summary>
    public string DriveLetter { get; set; } = string.Empty;

    /// <summary>Gets or sets the volume label shown in Explorer.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets the current file system (e.g. NTFS, FAT32).</summary>
    public string FileSystem { get; set; } = string.Empty;

    /// <summary>Gets or sets the total size in bytes.</summary>
    public ulong Capacity { get; set; }

    /// <summary>Gets or sets the free space in bytes.</summary>
    public ulong FreeSpace { get; set; }

    /// <summary>Display text for the list (e.g. "D: (Label) - NTFS - 500 GB").</summary>
    public string DisplayText => string.IsNullOrWhiteSpace(Label)
        ? $"{DriveLetter} - {FileSystem} - {FormatBytes(Capacity)}"
        : $"{DriveLetter} ({Label}) - {FileSystem} - {FormatBytes(Capacity)}";

    private static string FormatBytes(ulong bytes) => ByteFormatter.Format(bytes);
}
