using System.Text.Json;
using DriveStat.Models;

namespace DriveStat.Services;

/// <summary>
/// Persists extension→color mappings under LocalAppData so colors stay stable across scans.
/// </summary>
public static class ExtensionColorStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiskUtilities",
        "DriveStat",
        "extension-colors.json");

    public static Dictionary<string, Windows.UI.Color> Load()
    {
        var map = new Dictionary<string, Windows.UI.Color>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(StorePath))
            {
                return map;
            }

            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(StorePath));
            if (raw == null)
            {
                return map;
            }

            foreach (var (ext, argb) in raw)
            {
                if (TryParseArgb(argb, out var color))
                {
                    map[ext] = color;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warning("Failed to load extension color map.", ex, nameof(ExtensionColorStore));
        }

        return map;
    }

    public static void Save(IEnumerable<ExtensionInfo> extensions)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            var raw = extensions.ToDictionary(
                e => e.Extension,
                e => $"#{e.Color.A:X2}{e.Color.R:X2}{e.Color.G:X2}{e.Color.B:X2}",
                StringComparer.OrdinalIgnoreCase);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            AppLog.Warning("Failed to save extension color map.", ex, nameof(ExtensionColorStore));
        }
    }

    private static bool TryParseArgb(string text, out Windows.UI.Color color)
    {
        color = default;
        text = text.Trim();
        if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        if (text.Length == 6)
        {
            text = "FF" + text;
        }

        if (text.Length != 8 || !uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint value))
        {
            return false;
        }

        color = Windows.UI.Color.FromArgb(
            (byte)((value >> 24) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF));
        return true;
    }
}
