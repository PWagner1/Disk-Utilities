namespace DiskHealth.Models;

/// <summary>
/// A single S.M.A.R.T. attribute from a drive.
/// </summary>
public class SmartAttribute
{
    public byte Id { get; set; }
    public string IdHex => $"0x{Id:X2}";
    public string Name { get; set; } = string.Empty;
    public byte Value { get; set; }
    public byte Worst { get; set; }
    public ulong Raw { get; set; }
    public string DisplayValue => FormatRaw(Id, Raw);
    public bool IsCritical { get; set; }

    private static string FormatRaw(byte id, ulong raw)
    {
        return id switch
        {
            9 => $"{raw} hours",   // Power-On Hours
            12 => $"{raw}",        // Power Cycle Count
            194 => raw <= 255 ? $"{raw} °C" : $"{raw}",  // Temperature (may be raw)
            241 => raw > 0 ? FormatBytes(raw * 512) : $"{raw}",  // LBAs Written (sectors × 512)
            242 => raw > 0 ? FormatBytes(raw * 512) : $"{raw}",  // LBAs Read
            _ => raw.ToString()
        };
    }

    private static string FormatBytes(ulong bytes) => ByteFormatter.Format(bytes);
}
