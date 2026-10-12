#nullable enable

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
    private const string KeyUnallocatedColour = "DiagramColourUnallocated";
    private const string KeyTheme = "AppTheme";
    private const string KeyLastFileSystem = "FormatLastFileSystem";
    private const string KeyLastVolumeLabel = "FormatLastVolumeLabel";
    private const string KeyLastClusterSize = "FormatLastClusterSize";
    private const string KeyLastQuickFormat = "FormatLastQuickFormat";
    private const string KeyLastCompression = "FormatLastCompression";
    private const string KeyLastSecureDeletion = "FormatLastSecureDeletion";
    private const string KeyLastWipePresetIndex = "FormatLastWipePresetIndex";

    /// <summary>Theme values: Light, Dark, or Default (use system).</summary>
    public const string ThemeLight = "Light";

    /// <summary>Theme values: Light, Dark, or Default (use system).</summary>
    public const string ThemeDark = "Dark";

    /// <summary>Theme values: Light, Dark, or Default (use system).</summary>
    public const string ThemeDefault = "Default";

    /// <summary>Gets the app theme (Light, Dark, or Default).</summary>
    public static string GetTheme()
    {
        return GetString(KeyTheme, ThemeDefault);
    }

    /// <summary>Sets the app theme (Light, Dark, or Default).</summary>
    public static void SetTheme(string? value)
    {
        SetString(KeyTheme, value ?? ThemeDefault);
    }

    /// <summary>Default colour for formattable partition blocks (e.g. Steel Blue).</summary>
    public const string DefaultFormattableColour = "#FF4682B4";

    /// <summary>Default colour for the system drive (C:) block (e.g. Orange Red).</summary>
    public const string DefaultSystemDriveColour = "#FFFF4500";

    /// <summary>Default colour for partitions with no drive letter (e.g. Dark Gray).</summary>
    public const string DefaultNoLetterColour = "#FF4A4A4A";

    /// <summary>Default colour for unallocated space (e.g. Dark Gray).</summary>
    public const string DefaultUnallocatedColour = "#FF4A4A4A";

    /// <summary>Gets the diagram colour for formattable partitions (hex #AARRGGBB).</summary>
    public static string GetFormattableColour()
    {
        return GetString(KeyFormattableColour, DefaultFormattableColour);
    }

    /// <summary>Sets the diagram colour for formattable partitions (hex #AARRGGBB).</summary>
    public static void SetFormattableColour(string? value)
    {
        SetString(KeyFormattableColour, value ?? DefaultFormattableColour);
    }

    /// <summary>Gets the diagram colour for the system drive (C:) (hex #AARRGGBB).</summary>
    public static string GetSystemDriveColour()
    {
        return GetString(KeySystemDriveColour, DefaultSystemDriveColour);
    }

    /// <summary>Sets the diagram colour for the system drive (hex #AARRGGBB).</summary>
    public static void SetSystemDriveColour(string? value)
    {
        SetString(KeySystemDriveColour, value ?? DefaultSystemDriveColour);
    }

    /// <summary>Gets the diagram colour for partitions with no drive letter (hex #AARRGGBB).</summary>
    public static string GetNoLetterColour()
    {
        return GetString(KeyNoLetterColour, DefaultNoLetterColour);
    }

    /// <summary>Sets the diagram colour for partitions with no drive letter (hex #AARRGGBB).</summary>
    public static void SetNoLetterColour(string? value)
    {
        SetString(KeyNoLetterColour, value ?? DefaultNoLetterColour);
    }

    /// <summary>Gets the diagram colour for unallocated space (hex #AARRGGBB).</summary>
    public static string GetUnallocatedColour()
    {
        return GetString(KeyUnallocatedColour, DefaultUnallocatedColour);
    }

    /// <summary>Sets the diagram colour for unallocated space (hex #AARRGGBB).</summary>
    public static void SetUnallocatedColour(string? value)
    {
        SetString(KeyUnallocatedColour, value ?? DefaultUnallocatedColour);
    }

    /// <summary>Last-used format file system name (e.g. NTFS).</summary>
    public static string GetLastFileSystem() => GetString(KeyLastFileSystem, "NTFS");

    /// <summary>Persists last-used format file system name.</summary>
    public static void SetLastFileSystem(string? value) => SetString(KeyLastFileSystem, value ?? "NTFS");

    /// <summary>Last-used volume label (may be empty).</summary>
    public static string GetLastVolumeLabel() => GetString(KeyLastVolumeLabel, string.Empty);

    /// <summary>Persists last-used volume label.</summary>
    public static void SetLastVolumeLabel(string? value) => SetString(KeyLastVolumeLabel, value ?? string.Empty);

    /// <summary>Last-used cluster size in bytes (0 = default).</summary>
    public static uint GetLastClusterSize()
        => uint.TryParse(GetString(KeyLastClusterSize, "0"), out uint v) ? v : 0;

    /// <summary>Persists last-used cluster size in bytes.</summary>
    public static void SetLastClusterSize(uint value) => SetString(KeyLastClusterSize, value.ToString());

    /// <summary>Last-used quick format checkbox.</summary>
    public static bool GetLastQuickFormat() => GetBool(KeyLastQuickFormat, defaultValue: true);

    /// <summary>Persists last-used quick format checkbox.</summary>
    public static void SetLastQuickFormat(bool value) => SetBool(KeyLastQuickFormat, value);

    /// <summary>Last-used compression checkbox.</summary>
    public static bool GetLastCompression() => GetBool(KeyLastCompression, defaultValue: false);

    /// <summary>Persists last-used compression checkbox.</summary>
    public static void SetLastCompression(bool value) => SetBool(KeyLastCompression, value);

    /// <summary>Last-used free-space overwrite checkbox.</summary>
    public static bool GetLastSecureDeletion() => GetBool(KeyLastSecureDeletion, defaultValue: false);

    /// <summary>Persists last-used free-space overwrite checkbox.</summary>
    public static void SetLastSecureDeletion(bool value) => SetBool(KeyLastSecureDeletion, value);

    /// <summary>Last-used wipe preset index (0-based).</summary>
    public static int GetLastWipePresetIndex()
        => int.TryParse(GetString(KeyLastWipePresetIndex, "0"), out int v) ? Math.Max(0, v) : 0;

    /// <summary>Persists last-used wipe preset index.</summary>
    public static void SetLastWipePresetIndex(int value) => SetString(KeyLastWipePresetIndex, Math.Max(0, value).ToString());

    private static bool GetBool(string key, bool defaultValue)
    {
        string s = GetString(key, defaultValue ? "1" : "0");
        return s is "1" or "true" or "True";
    }

    private static void SetBool(string key, bool value) => SetString(key, value ? "1" : "0");

    private static string GetString(string key, string defaultValue)
    {
        try
        {
            var settings = ApplicationData.Current?.LocalSettings?.Values;
            if (settings != null && settings.TryGetValue(key, out var obj) && obj is string s)
            {
                return s;
            }
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
            {
                settings[key] = value;
            }
        }
        catch { /* ignore */ }
    }

    /// <summary>Parses a hex colour string (#AARRGGBB or #RRGGBB) to a WinUI Color. Returns null if invalid.</summary>
    public static Windows.UI.Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 6)
        {
            hex = "FF" + hex;
        }

        if (hex.Length != 8)
        {
            return null;
        }

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
    [
        ("Steel Blue", "#FF4682B4"),
        ("Orange Red", "#FFFF4500"),
        ("Dark Gray", "#FF4A4A4A"),
        ("Forest Green", "#FF228B22"),
        ("Dark Orange", "#FFFF8C00"),
        ("Medium Purple", "#FF9370DB"),
        ("Teal", "#FF008080"),
        ("Crimson", "#FFDC143C"),
        ("Slate Blue", "#FF6A5ACD"),
        ("Chocolate", "#FFD2691E")
    ];
}
