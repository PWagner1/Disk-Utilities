using System.Globalization;
using System.Text;
using DriveStat.Models;

namespace DriveStat.Services;

/// <summary>
/// Builds CSV/HTML disk-usage reports from a scanned tree.
/// </summary>
public static class ExportService
{
    public static string BuildCsv(DirectoryNode root, IReadOnlyList<ExtensionInfo> extensions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Section,Name,Path,SizeBytes,IsDirectory,Extension,FileCount,Percentage");
        AppendCsvNode(sb, root);
        foreach (var ext in extensions)
        {
            sb.Append("Extension,")
                .Append(Csv(ext.Extension)).Append(',')
                .Append(',')
                .Append(ext.Size.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(',')
                .Append(Csv(ext.Extension)).Append(',')
                .Append(ext.FileCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(ext.Percentage.ToString("0.###", CultureInfo.InvariantCulture))
                .AppendLine();
        }

        return sb.ToString();
    }

    public static string BuildHtml(DirectoryNode root, IReadOnlyList<ExtensionInfo> extensions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>");
        sb.AppendLine("<title>DriveStat report</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,sans-serif;margin:24px}table{border-collapse:collapse;width:100%;margin:16px 0}th,td{border:1px solid #ccc;padding:6px 8px;text-align:left}th{background:#f3f3f3}.swatch{display:inline-block;width:12px;height:12px;margin-right:6px;border-radius:2px}</style>");
        sb.AppendLine("</head><body>");
        sb.Append("<h1>DriveStat report</h1>");
        sb.Append("<p><strong>Root:</strong> ").Append(Html(root.FullPath)).Append("<br/>");
        sb.Append("<strong>Total:</strong> ").Append(Html(DirectoryNode.FormatSize(root.Size)));
        sb.Append(" (").Append(root.Size.ToString("N0", CultureInfo.InvariantCulture)).Append(" bytes)</p>");

        sb.AppendLine("<h2>Extensions</h2><table><tr><th>Extension</th><th>Files</th><th>Size</th><th>%</th></tr>");
        foreach (var ext in extensions)
        {
            string color = $"#{ext.Color.R:X2}{ext.Color.G:X2}{ext.Color.B:X2}";
            sb.Append("<tr><td><span class=\"swatch\" style=\"background:").Append(color).Append("\"></span>")
                .Append(Html(ext.Extension)).Append("</td><td>")
                .Append(ext.FileCount.ToString("N0", CultureInfo.InvariantCulture)).Append("</td><td>")
                .Append(Html(ext.SizeFormatted)).Append("</td><td>")
                .Append(ext.Percentage.ToString("0.##", CultureInfo.InvariantCulture)).Append("%</td></tr>");
        }

        sb.AppendLine("</table>");
        sb.AppendLine("<h2>Largest items (top level)</h2><table><tr><th>Name</th><th>Type</th><th>Size</th><th>Path</th></tr>");
        foreach (var child in root.Children.OrderByDescending(c => c.Size).Take(100))
        {
            sb.Append("<tr><td>").Append(Html(child.Name)).Append("</td><td>")
                .Append(child.IsDirectory ? "Folder" : "File").Append("</td><td>")
                .Append(Html(child.FormattedSize)).Append("</td><td>")
                .Append(Html(child.FullPath)).Append("</td></tr>");
        }

        sb.AppendLine("</table></body></html>");
        return sb.ToString();
    }

    private static void AppendCsvNode(StringBuilder sb, DirectoryNode node)
    {
        sb.Append("Node,")
            .Append(Csv(node.Name)).Append(',')
            .Append(Csv(node.FullPath)).Append(',')
            .Append(node.Size.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(node.IsDirectory ? "true" : "false").Append(',')
            .Append(Csv(node.Extension)).Append(',')
            .Append(',')
            .AppendLine();

        foreach (var child in node.Children)
        {
            AppendCsvNode(sb, child);
        }
    }

    private static string Csv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    private static string Html(string value)
        => System.Net.WebUtility.HtmlEncode(value);
}
