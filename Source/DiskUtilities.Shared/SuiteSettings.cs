using System.Text.Json;

namespace DiskUtilities.Shared;

/// <summary>
/// Suite-wide settings seed under <c>%LocalAppData%\DiskUtilities\suite-settings.json</c>.
/// Theme and destructive-prompt preference; individual apps may also keep local overrides.
/// </summary>
public static class SuiteSettings
{
    public const string ThemeLight = "Light";
    public const string ThemeDark = "Dark";
    public const string ThemeDefault = "Default";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly object Gate = new();
    private static SuiteSettingsData? _cache;

    /// <summary>Path to the suite settings file.</summary>
    public static string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiskUtilities",
        "suite-settings.json");

    /// <summary>Gets the suite theme (Light, Dark, or Default).</summary>
    public static string GetTheme()
    {
        string theme = Load().Theme ?? ThemeDefault;
        return theme is ThemeLight or ThemeDark or ThemeDefault ? theme : ThemeDefault;
    }

    /// <summary>Sets the suite theme (Light, Dark, or Default).</summary>
    public static void SetTheme(string? theme)
    {
        var data = Load();
        data.Theme = theme is ThemeLight or ThemeDark ? theme : ThemeDefault;
        Save(data);
    }

    /// <summary>
    /// When true (default), data-destroying ops should use acknowledgement checkboxes.
    /// Cannot disable confirmations entirely — only whether the extra acknowledgement UI is preferred.
    /// </summary>
    public static bool GetRequireDestructiveAcknowledgement() => Load().RequireDestructiveAcknowledgement;

    /// <summary>Sets whether acknowledgement checkboxes are preferred for destructive ops.</summary>
    public static void SetRequireDestructiveAcknowledgement(bool value)
    {
        var data = Load();
        data.RequireDestructiveAcknowledgement = value;
        Save(data);
    }

    private static SuiteSettingsData Load()
    {
        lock (Gate)
        {
            if (_cache != null)
            {
                return Clone(_cache);
            }

            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    _cache = JsonSerializer.Deserialize<SuiteSettingsData>(json, JsonOptions) ?? new SuiteSettingsData();
                }
                else
                {
                    _cache = new SuiteSettingsData();
                }
            }
            catch
            {
                _cache = new SuiteSettingsData();
            }

            return Clone(_cache);
        }
    }

    private static void Save(SuiteSettingsData data)
    {
        lock (Gate)
        {
            _cache = Clone(data);
            try
            {
                string? dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_cache, JsonOptions));
            }
            catch (Exception ex)
            {
                AppLog.Warning("Failed to save suite settings.", ex, nameof(SuiteSettings));
            }
        }
    }

    private static SuiteSettingsData Clone(SuiteSettingsData d) => new()
    {
        Theme = d.Theme,
        RequireDestructiveAcknowledgement = d.RequireDestructiveAcknowledgement
    };

    private sealed class SuiteSettingsData
    {
        public string Theme { get; set; } = ThemeDefault;
        public bool RequireDestructiveAcknowledgement { get; set; } = true;
    }
}
