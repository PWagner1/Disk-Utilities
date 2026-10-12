namespace DiskEraser.Models;

/// <summary>Bus / protocol classification for hardware erase capability queries.</summary>
public enum HardwareEraseBus
{
    Unknown,
    Ata,
    Nvme
}

/// <summary>
/// Result of probing a physical disk for ATA Secure Erase / NVMe Sanitize support.
/// Detection only — does not certify wipe standards or remapped-cell erasure.
/// </summary>
public sealed class HardwareEraseCapability
{
    public int DiskIndex { get; init; }
    public string Model { get; init; } = string.Empty;
    public HardwareEraseBus Bus { get; init; }
    public bool IsAdminRequired { get; init; } = true;

    /// <summary>ATA: security feature set present and erase supported.</summary>
    public bool AtaSecureEraseSupported { get; init; }

    /// <summary>ATA: security is frozen (common after boot; erase typically blocked).</summary>
    public bool AtaSecurityFrozen { get; init; }

    /// <summary>NVMe: Sanitize Crypto Erase supported (SANICAP bit).</summary>
    public bool NvmeSanitizeCryptoErase { get; init; }

    /// <summary>NVMe: Sanitize Block Erase supported.</summary>
    public bool NvmeSanitizeBlockErase { get; init; }

    /// <summary>NVMe: Sanitize Overwrite supported.</summary>
    public bool NvmeSanitizeOverwrite { get; init; }

    /// <summary>True when any hardware erase method appears available and not blocked.</summary>
    public bool CanAttemptHardwareErase =>
        Bus == HardwareEraseBus.Nvme
            ? (NvmeSanitizeCryptoErase || NvmeSanitizeBlockErase || NvmeSanitizeOverwrite)
            : Bus == HardwareEraseBus.Ata && AtaSecureEraseSupported && !AtaSecurityFrozen;

    /// <summary>Short UI summary of what was detected.</summary>
    public string Summary
    {
        get
        {
            if (Bus == HardwareEraseBus.Nvme)
            {
                var parts = new List<string>();
                if (NvmeSanitizeCryptoErase)
                {
                    parts.Add("Crypto Erase");
                }

                if (NvmeSanitizeBlockErase)
                {
                    parts.Add("Block Erase");
                }

                if (NvmeSanitizeOverwrite)
                {
                    parts.Add("Overwrite");
                }

                return parts.Count == 0
                    ? "NVMe: Sanitize not advertised (or query failed)."
                    : "NVMe Sanitize: " + string.Join(", ", parts);
            }

            if (Bus == HardwareEraseBus.Ata)
            {
                if (!AtaSecureEraseSupported)
                {
                    return "ATA: Secure Erase not supported (or query failed).";
                }

                return AtaSecurityFrozen
                    ? "ATA: Secure Erase supported but security is frozen (typical after boot)."
                    : "ATA: Secure Erase supported.";
            }

            return "Unknown bus — hardware erase not available.";
        }
    }
}
