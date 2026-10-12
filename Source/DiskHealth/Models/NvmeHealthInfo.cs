namespace DiskHealth.Models;

/// <summary>
/// NVMe SMART / Health Information Log (Log Identifier 02h) summary fields.
/// </summary>
public sealed class NvmeHealthInfo
{
    public byte CriticalWarning { get; init; }
    public int TemperatureCelsius { get; init; }
    public byte AvailableSparePercent { get; init; }
    public byte AvailableSpareThreshold { get; init; }
    public byte PercentageUsed { get; init; }
    public ulong DataUnitsRead { get; init; }
    public ulong DataUnitsWritten { get; init; }
    public ulong PowerCycles { get; init; }
    public ulong PowerOnHours { get; init; }
    public ulong UnsafeShutdowns { get; init; }
    public ulong MediaErrors { get; init; }

    public bool HasCriticalWarning => CriticalWarning != 0;

    public string TemperatureDisplay => $"{TemperatureCelsius} °C";
    public string AvailableSpareDisplay => $"{AvailableSparePercent}% (threshold {AvailableSpareThreshold}%)";
    public string PercentageUsedDisplay => $"{PercentageUsed}%";
    public string DataWrittenDisplay => FormatDataUnits(DataUnitsWritten);
    public string DataReadDisplay => FormatDataUnits(DataUnitsRead);
    public string PowerOnHoursDisplay => $"{PowerOnHours:N0} hours";

    private static string FormatDataUnits(ulong units)
    {
        // NVMe data units are typically 1000 × 512 bytes.
        double bytes = units * 1000.0 * 512.0;
        return ByteFormatter.Format((ulong)Math.Min(bytes, ulong.MaxValue));
    }
}
