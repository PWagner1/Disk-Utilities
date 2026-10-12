namespace DiskUtilities.Shared;

/// <summary>
/// Formats byte counts for UI display (e.g. "1.5 GB").
/// </summary>
public static class ByteFormatter
{
    private static readonly string[] Sizes = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>Formats a non-negative byte count using binary (1024) units.</summary>
    public static string Format(ulong bytes)
    {
        int order = 0;
        double len = bytes;
        while (len >= 1024 && order < Sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {Sizes[order]}";
    }

    /// <summary>Formats a byte count; negative values are treated as zero.</summary>
    public static string Format(long bytes)
        => Format(bytes < 0 ? 0UL : (ulong)bytes);
}
