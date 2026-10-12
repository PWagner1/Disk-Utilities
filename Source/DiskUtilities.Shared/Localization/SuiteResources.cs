using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace DiskUtilities.Shared.Localization;

/// <summary>
/// Lightweight string table (en first). Apps can call Get(key) instead of hard-coding UI copy.
/// Additional locales: ship <c>{culture}.json</c> next to the assembly or as embedded resources later.
/// </summary>
public static class SuiteResources
{
    private static readonly ConcurrentDictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase);
    private static string _culture = "en";
    private static bool _loaded;

    /// <summary>Active culture name (e.g. en).</summary>
    public static string Culture => _culture;

    /// <summary>Loads embedded <c>en.json</c> (and optional satellite file).</summary>
    public static void EnsureLoaded(string? culture = null)
    {
        if (_loaded && (culture == null || string.Equals(culture, _culture, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _culture = string.IsNullOrWhiteSpace(culture) ? "en" : culture.Trim();
        Map.Clear();
        LoadEmbedded("en");
        if (!string.Equals(_culture, "en", StringComparison.OrdinalIgnoreCase))
        {
            LoadEmbedded(_culture);
        }

        _loaded = true;
    }

    /// <summary>Gets a localized string; returns <paramref name="key"/> if missing.</summary>
    public static string Get(string key)
    {
        EnsureLoaded();
        return Map.TryGetValue(key, out string? value) ? value : key;
    }

    /// <summary>Gets a localized string or a fallback.</summary>
    public static string Get(string key, string fallback)
    {
        EnsureLoaded();
        return Map.TryGetValue(key, out string? value) ? value : fallback;
    }

    private static void LoadEmbedded(string culture)
    {
        var asm = typeof(SuiteResources).Assembly;
        string resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($".Localization.{culture}.json", StringComparison.OrdinalIgnoreCase)
                                 || n.EndsWith($"Localization.{culture}.json", StringComparison.OrdinalIgnoreCase)
                                 || n.EndsWith($"{culture}.json", StringComparison.OrdinalIgnoreCase) && n.Contains("Localization", StringComparison.OrdinalIgnoreCase))
            ?? string.Empty;

        if (string.IsNullOrEmpty(resourceName))
        {
            // Fallback: file next to assembly
            string? dir = Path.GetDirectoryName(asm.Location);
            if (!string.IsNullOrEmpty(dir))
            {
                string path = Path.Combine(dir, "Localization", culture + ".json");
                if (File.Exists(path))
                {
                    MergeJson(File.ReadAllText(path));
                }
            }

            return;
        }

        using Stream? stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return;
        }

        using var reader = new StreamReader(stream);
        MergeJson(reader.ReadToEnd());
    }

    private static void MergeJson(string json)
    {
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict == null)
            {
                return;
            }

            foreach (var kv in dict)
            {
                Map[kv.Key] = kv.Value;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warning("Failed to parse localization JSON.", ex, nameof(SuiteResources));
        }
    }
}
