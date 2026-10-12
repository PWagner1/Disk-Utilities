namespace DiskHealth.Services;

/// <summary>
/// Service for retrieving S.M.A.R.T. (Self-Monitoring, Analysis and Reporting Technology) stats from physical drives.
/// Uses WMI root\WMI classes: MSWmi_PnPDeviceId, MSStorageDriver_FailurePredictStatus, MSStorageDriver_ATAPISmartData.
/// </summary>
public static class SmartService
{
    private static readonly Dictionary<byte, (string Name, bool Critical)> AttributeNames = new()
    {
        [0x01] = ("Raw Read Error Rate", true),
        [0x05] = ("Reallocated Sector Count", true),
        [0x09] = ("Power-On Hours", false),
        [0x0A] = ("Spin Retry Count", true),
        [0x0C] = ("Power Cycle Count", false),
        [0xB7] = ("SATA Downshift Error Count", false),
        [0xBB] = ("Reported Uncorrectable Errors", true),
        [0xBC] = ("Command Timeout", true),
        [0xBD] = ("High Fly Writes", false),
        [0xBE] = ("Airflow Temperature", false),
        [0xC0] = ("Power-Off Retract Count", false),
        [0xC1] = ("Load Cycle Count", false),
        [0xC2] = ("Temperature", false),
        [0xC4] = ("Reallocated Event Count", true),
        [0xC5] = ("Current Pending Sector", true),
        [0xC6] = ("Uncorrectable Sector Count", true),
        [0xC7] = ("UltraDMA CRC Error Count", false),
        [0xF1] = ("Total LBAs Written", false),
        [0xF2] = ("Total LBAs Read", false),
        [0xF9] = ("Total LBAs Written", false),
        [0xFA] = ("Total LBAs Read", false),
    };

    private const int AttributeSize = 12;

    /// <summary>
    /// Gets physical drives with S.M.A.R.T. status and attributes.
    /// Uses WMI root\WMI for ATA SMART; NVMe drives also query Health Information Log via Storage API.
    /// </summary>
    public static IReadOnlyList<SmartDriveInfo> GetSmartDrives()
    {
        var list = new List<SmartDriveInfo>();
        try
        {
            Dictionary<string, string>? pnpMap = null;
            Dictionary<string, (bool?, uint?)>? failureMap = null;
            Dictionary<string, byte[]>? smartMap = null;

            Parallel.Invoke(
                () => pnpMap = GetPnpDeviceIdToInstanceNameMap(),
                () => failureMap = GetFailurePredictStatusMap(),
                () => smartMap = GetSmartDataMap()
            );

            pnpMap ??= [];
            failureMap ??= [];
            smartMap ??= [];

            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Index, Size, Model, PNPDeviceID, InterfaceType FROM Win32_DiskDrive");

            foreach (ManagementBaseObject obj in searcher.Get())
            {
                var pnpId = obj["PNPDeviceID"]?.ToString()?.Trim();
                if (string.IsNullOrEmpty(pnpId))
                    continue;

                // Match InstanceName to PNPDeviceID. Try: 1) direct match in smart/failure maps, 2) MSWmi_PnPDeviceId mapping
                var instanceName = FindMatchingInstanceName(pnpId, smartMap.Keys)
                    ?? FindMatchingInstanceName(pnpId, failureMap.Keys)
                    ?? pnpMap!.GetValueOrDefault(pnpId);

                var (predictFailure, reason) = instanceName != null ? failureMap.GetValueOrDefault(instanceName, (null, null)) : (null, null);
                var vendorSpecific = instanceName != null ? smartMap.GetValueOrDefault(instanceName) : null;
                int index = obj["Index"] != null ? Convert.ToInt32(obj["Index"]) : -1;
                string model = obj["Model"]?.ToString()?.Trim() ?? "Unknown";
                string interfaceType = obj["InterfaceType"]?.ToString()?.Trim() ?? string.Empty;
                bool isNvme = NvmeHealthReader.IsLikelyNvme(interfaceType, pnpId, model);
                NvmeHealthInfo? nvme = null;
                if (isNvme && index >= 0)
                {
                    try
                    {
                        nvme = NvmeHealthReader.TryRead(index);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warning($"NVMe health log unavailable for disk {index}.", ex, nameof(SmartService));
                    }
                }

                var attrs = ParseSmartAttributes(vendorSpecific);
                if (nvme != null)
                {
                    attrs = MergeNvmeAsAttributes(attrs, nvme);
                }

                list.Add(new SmartDriveInfo
                {
                    Index = index,
                    Model = model,
                    Size = obj["Size"] != null ? Convert.ToUInt64(obj["Size"]) : 0,
                    InterfaceType = interfaceType,
                    PnpDeviceId = pnpId,
                    PredictFailure = predictFailure,
                    Reason = reason,
                    Attributes = attrs,
                    IsNvme = isNvme,
                    NvmeHealth = nvme
                });
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to enumerate S.M.A.R.T. drives via WMI.", ex, nameof(SmartService));
        }

        return list.OrderBy(d => d.Index).ToList();
    }

    private static IReadOnlyList<SmartAttribute> MergeNvmeAsAttributes(
        IReadOnlyList<SmartAttribute> existing,
        NvmeHealthInfo nvme)
    {
        var list = existing.ToList();
        void AddSynthetic(byte id, string name, ulong raw, bool critical = false)
        {
            if (list.Any(a => a.Id == id))
            {
                return;
            }

            list.Add(new SmartAttribute
            {
                Id = id,
                Name = name,
                Value = 100,
                Worst = 100,
                Raw = raw,
                IsCritical = critical
            });
        }

        AddSynthetic(0xC2, "NVMe Temperature (°C)", (ulong)Math.Max(0, nvme.TemperatureCelsius));
        AddSynthetic(0xE4, "NVMe Available Spare (%)", nvme.AvailableSparePercent);
        AddSynthetic(0xE5, "NVMe Percentage Used (%)", nvme.PercentageUsed, critical: nvme.PercentageUsed >= 90);
        AddSynthetic(0xE6, "NVMe Power-On Hours", nvme.PowerOnHours);
        AddSynthetic(0xE7, "NVMe Media Errors", nvme.MediaErrors, critical: nvme.MediaErrors > 0);
        AddSynthetic(0xE8, "NVMe Unsafe Shutdowns", nvme.UnsafeShutdowns);
        return list.OrderBy(a => a.Id).ToList();
    }

    /// <summary>
    /// Finds an InstanceName that matches the given PNPDeviceID.
    /// InstanceName may equal PNPDeviceID (case-insensitive) or have a trailing suffix like _0.
    /// </summary>
    private static string? FindMatchingInstanceName(string pnpDeviceId, IEnumerable<string> instanceNames)
    {
        foreach (var iname in instanceNames)
        {
            if (string.IsNullOrEmpty(iname)) continue;
            if (string.Equals(iname, pnpDeviceId, StringComparison.OrdinalIgnoreCase))
                return iname;
            if (iname.StartsWith(pnpDeviceId, StringComparison.OrdinalIgnoreCase) && iname.Length > pnpDeviceId.Length && iname[pnpDeviceId.Length] == '_')
                return iname;
        }
        return null;
    }

    /// <summary>
    /// Async version of GetSmartDrives for non-blocking UI.
    /// </summary>
    public static async Task<IReadOnlyList<SmartDriveInfo>> GetSmartDrivesAsync(CancellationToken ct = default)
    {
        return await Task.Run(() => GetSmartDrives(), ct).ConfigureAwait(false);
    }

    private static Dictionary<string, string> GetPnpDeviceIdToInstanceNameMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT PnPDeviceId, InstanceName FROM MSWmi_PnPDeviceId");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                var pnpId = obj["PnPDeviceId"]?.ToString()?.TrimEnd(' ', '\0');
                var instanceName = obj["InstanceName"]?.ToString()?.TrimEnd(' ', '\0');
                if (!string.IsNullOrEmpty(pnpId) && !string.IsNullOrEmpty(instanceName))
                    map[pnpId] = instanceName;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warning("MSWmi_PnPDeviceId map unavailable (WMI).", ex, nameof(SmartService));
        }

        return map;
    }

    private static Dictionary<string, (bool? PredictFailure, uint? Reason)> GetFailurePredictStatusMap()
    {
        var map = new Dictionary<string, (bool?, uint?)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, PredictFailure, Reason FROM MSStorageDriver_FailurePredictStatus");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                var instanceName = obj["InstanceName"]?.ToString()?.TrimEnd(' ', '\0');
                if (string.IsNullOrEmpty(instanceName))
                {
                    continue;
                }

                bool? predictFailure = obj["PredictFailure"] != null ? Convert.ToBoolean(obj["PredictFailure"]) : null;
                uint? reason = obj["Reason"] != null ? Convert.ToUInt32(obj["Reason"]) : null;
                map[instanceName] = (predictFailure, reason);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warning("FailurePredictStatus map unavailable (WMI).", ex, nameof(SmartService));
        }

        return map;
    }

    private static Dictionary<string, byte[]> GetSmartDataMap()
    {
        var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, VendorSpecific FROM MSStorageDriver_ATAPISmartData");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                var instanceName = obj["InstanceName"]?.ToString()?.TrimEnd(' ', '\0');
                var data = obj["VendorSpecific"] as byte[];
                if (!string.IsNullOrEmpty(instanceName) && data is { Length: >= 2 + AttributeSize })
                {
                    map[instanceName] = data;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warning("ATAPISmartData map unavailable (common for NVMe-only systems).", ex, nameof(SmartService));
        }

        return map;
    }

    private static IReadOnlyList<SmartAttribute> ParseSmartAttributes(byte[]? vendorSpecific)
    {
        var list = new List<SmartAttribute>();
        if (vendorSpecific == null || vendorSpecific.Length < 2 + AttributeSize)
        {
            return list;
        }

        for (int offset = 2; offset <= vendorSpecific.Length - AttributeSize; offset += AttributeSize)
        {
            byte id = vendorSpecific[offset];
            if (id == 0)
            {
                continue;
            }

            byte value = vendorSpecific[offset + 3];
            byte worst = vendorSpecific[offset + 4];
            // Raw value: 6 bytes little-endian at offset 5
            ulong raw = vendorSpecific[offset + 5]
                | ((ulong)vendorSpecific[offset + 6] << 8)
                | ((ulong)vendorSpecific[offset + 7] << 16)
                | ((ulong)vendorSpecific[offset + 8] << 24)
                | ((ulong)vendorSpecific[offset + 9] << 32)
                | ((ulong)vendorSpecific[offset + 10] << 40);

            var (name, critical) = AttributeNames.GetValueOrDefault(id, ($"Attribute 0x{id:X2}", false));

            list.Add(new SmartAttribute
            {
                Id = id,
                Name = name,
                Value = value,
                Worst = worst,
                Raw = raw,
                IsCritical = critical
            });
        }

        return list.OrderBy(a => a.Id).ToList();
    }
}
