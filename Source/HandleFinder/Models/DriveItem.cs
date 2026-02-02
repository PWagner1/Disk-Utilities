namespace HandleFinder.Models;

/// <summary>
/// Represents a drive for the drive selector combo.
/// </summary>
public sealed class DriveItem
{
    /// <summary>Full path to the drive root (e.g. C:\).</summary>
    public string Path { get; }

    /// <summary>Display text (e.g. C:).</summary>
    public string DisplayText => Path.TrimEnd('\\').TrimEnd(':') + ":";

    public DriveItem(string path)
    {
        Path = path;
    }
}
