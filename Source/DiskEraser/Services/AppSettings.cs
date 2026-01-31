using Microsoft.UI;
using Windows.Storage;

namespace DiskEraser.Services;

/// <summary>
/// Persists user settings (e.g. diagram colours) to local app storage.
/// Uses ApplicationData.LocalSettings when available; falls back to defaults otherwise.
/// </summary>
public static class AppSettings
{
    private const string KeyFormattableColour = "DiagramColourFormattable";
    private const string KeySystemDriveColour = "DiagramColourSystemDrive";
    private const string KeyNoLetterColour = "DiagramColourNoLetter";

    /// <summary>Default colour for formattable partition blocks (e.g. Steel Blue).</summary>
    public const string DefaultFormattableColour = "#FF4682B4";

    /// <summary>Default colour for the system drive (C:) block (e.g. Orange Red).</summary>
    public const string DefaultSystemDriveColour = "#FFFF4500";

    /// <summary>Default colour for partitions with no drive letter (e.g. Dark Gray).</summary>
    public const string DefaultNoLetterColour = "#FF4A4A4A";

    /// <summary>Gets the diagram colour for formattable partitions (hex #AARRGGBB).</summary>
    public static string GetFormattableColour()
    {
        return GetString(KeyFormattableColour, DefaultFormattableColour);
    }

    /// <summary>Sets the diagram colour for formattable partitions (hex #AARRGGBB).</summary>
    public static void SetFormattableColour(string value)
    {
        SetString(KeyFormattableColour, value ?? DefaultFormattableColour);
    }

    /// <summary>Gets the diagram colour for the system drive (C:) (hex #AARRGGBB).</summary>
    public static string GetSystemDriveColour()
    {
        return GetString(KeySystemDriveColour, DefaultSystemDriveColour);
    }

    /// <summary>Sets the diagram colour for the system drive (hex #AARRGGBB).</summary>
    public static void SetSystemDriveColour(string value)
    {
        SetString(KeySystemDriveColour, value ?? DefaultSystemDriveColour);
    }

    /// <summary>Gets the diagram colour for partitions with no drive letter (hex #AARRGGBB).</summary>
    public static string GetNoLetterColour()
    {
        return GetString(KeyNoLetterColour, DefaultNoLetterColour);
    }

    /// <summary>Sets the diagram colour for partitions with no drive letter (hex #AARRGGBB).</summary>
    public static void SetNoLetterColour(string value)
    {
        SetString(KeyNoLetterColour, value ?? DefaultNoLetterColour);
    }

    private static string GetString(string key, string defaultValue)
    {
        try
        {
            var settings = ApplicationData.Current?.LocalSettings?.Values;
            if (settings != null && settings.TryGetValue(key, out var obj) && obj is string s)
                return s;
        }
        catch { /* ignore */ }
        return defaultValue;
    }

    private static void SetString(string key, string value)
    {
        try
        {
            var settings = ApplicationData.Current?.LocalSettings?.Values;
            if (settings != null)
                settings[key] = value;
        }
        catch { /* ignore */ }
    }

    /// <summary>Parses a hex colour string (#AARRGGBB or #RRGGBB) to a WinUI Color. Returns null if invalid.</summary>
    public static Windows.UI.Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 6)
            hex = "FF" + hex;
        if (hex.Length != 8) return null;
        try
        {
            uint v = Convert.ToUInt32(hex, 16);
            return Windows.UI.Color.FromArgb(
                (byte)(v >> 24),
                (byte)(v >> 16),
                (byte)(v >> 8),
                (byte)(v));
        }
        catch { return null; }
    }

    /// <summary>Preset colour options for the Settings UI (display name, hex #AARRGGBB).</summary>
    public static readonly (string Name, string Hex)[] ColourPresets =
    {
        ("Steel Blue", "#FF4682B4"),
        ("Orange Red", "#FFFF4500"),
        ("Dark Gray", "#FF4A4A4A"),
        ("Forest Green", "#FF228B22"),
        ("Dark Orange", "#FFFF8C00"),
        ("Medium Purple", "#FF9370DB"),
        ("Teal", "#FF008080"),
        ("Crimson", "#FFDC143C"),
        ("Slate Blue", "#FF6A5ACD"),
        ("Chocolate", "#FFD2691E"),
    };
}
