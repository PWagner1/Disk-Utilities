using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DiskHealth.Services;

/// <summary>Exports carved-candidate lists to CSV or JSON.</summary>
public static class RecoveryExport
{
    public static string ToCsv(IEnumerable<RecoverableFileInfo> files)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DisplayName,FileType,Source,Offset,EstimatedSize,RecoveryChancePercent,PathHint,IsSelected");
        foreach (var f in files)
        {
            sb.Append(Csv(f.DisplayName)).Append(',')
                .Append(Csv(f.FileType)).Append(',')
                .Append(Csv(f.SourceLabel)).Append(',')
                .Append(f.Offset.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(f.EstimatedSize.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(f.RecoveryChancePercent.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Csv(f.PathHint ?? "")).Append(',')
                .Append(f.IsSelected ? "true" : "false")
                .AppendLine();
        }

        return sb.ToString();
    }

    public static string ToJson(IEnumerable<RecoverableFileInfo> files)
        => JsonSerializer.Serialize(
            files.Select(f => new
            {
                f.DisplayName,
                f.FileType,
                Source = f.SourceLabel,
                f.Offset,
                f.EstimatedSize,
                f.RecoveryChancePercent,
                f.PathHint,
                f.IsSelected
            }),
            new JsonSerializerOptions { WriteIndented = true });

    private static string Csv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
