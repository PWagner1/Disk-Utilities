namespace DiskHealth.Models;

/// <summary>
/// Physical drive info with S.M.A.R.T. status and attributes.
/// </summary>
public class SmartDriveInfo
{
    public int Index { get; set; }
    public string Model { get; set; } = string.Empty;
    public ulong Size { get; set; }
    public string InterfaceType { get; set; } = string.Empty;
    public string DisplayText => $"Disk {Index}: {Model?.Trim() ?? "Unknown"} ({FormatSize(Size)}){NvmeSuffix}";
    public bool? PredictFailure { get; set; }
    public uint? Reason { get; set; }
    public string HealthStatus =>
        NvmeHealth?.HasCriticalWarning == true ? "⚠️ NVMe critical warning"
        : PredictFailure == true ? "⚠️ Failure predicted"
        : PredictFailure == false ? "✓ OK"
        : NvmeHealth != null ? "✓ NVMe health log"
        : "—";
    public IReadOnlyList<SmartAttribute> Attributes { get; set; } = [];
    public string PnpDeviceId { get; set; } = string.Empty;
    public bool IsNvme { get; set; }
    public NvmeHealthInfo? NvmeHealth { get; set; }

    public int? TemperatureCelsius =>
        NvmeHealth?.TemperatureCelsius
        ?? FindTemperatureFromAta();

    public string TemperatureDisplay => TemperatureCelsius is int t ? $"{t} °C" : "—";
    public string AvailableSpareDisplay => NvmeHealth?.AvailableSpareDisplay ?? "—";
    public string PercentageUsedDisplay => NvmeHealth?.PercentageUsedDisplay ?? "—";
    public string WearDisplay => NvmeHealth != null ? NvmeHealth.PercentageUsedDisplay : "—";

    private string NvmeSuffix => IsNvme ? " [NVMe]" : "";

    private int? FindTemperatureFromAta()
    {
        var temp = Attributes.FirstOrDefault(a => a.Id is 0xC2 or 0xBE);
        if (temp == null)
        {
            return null;
        }

        // Common encoding: low byte is Celsius.
        ulong raw = temp.Raw & 0xFF;
        return raw is > 0 and < 120 ? (int)raw : null;
    }

    private static string FormatSize(ulong bytes)
    {
        if (bytes >= 1UL << 40)
        {
            return $"{bytes >> 40} TB";
        }

        if (bytes >= 1UL << 30)
        {
            return $"{bytes >> 30} GB";
        }

        if (bytes >= 1UL << 20)
        {
            return $"{bytes >> 20} MB";
        }

        return $"{bytes} B";
    }
}
