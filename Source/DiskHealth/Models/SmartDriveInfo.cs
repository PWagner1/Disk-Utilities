namespace DiskHealth.Models;

/// <summary>
/// Physical drive info with S.M.A.R.T. status and attributes.
/// </summary>
public class SmartDriveInfo
{
    public int Index { get; set; }
    public string Model { get; set; } = string.Empty;
    public ulong Size { get; set; }
    public string DisplayText => $"Disk {Index}: {Model?.Trim() ?? "Unknown"} ({FormatSize(Size)})";
    public bool? PredictFailure { get; set; }
    public uint? Reason { get; set; }
    public string HealthStatus => PredictFailure == true ? "⚠️ Failure predicted" : PredictFailure == false ? "✓ OK" : "—";
    public IReadOnlyList<SmartAttribute> Attributes { get; set; } = [];
    public string PNPDeviceID { get; set; } = string.Empty;

    private static string FormatSize(ulong bytes)
    {
        if (bytes >= 1UL << 40) return $"{bytes >> 40} TB";
        if (bytes >= 1UL << 30) return $"{bytes >> 30} GB";
        if (bytes >= 1UL << 20) return $"{bytes >> 20} MB";
        return $"{bytes} B";
    }
}
