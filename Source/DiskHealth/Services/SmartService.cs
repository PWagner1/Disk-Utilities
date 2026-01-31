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
    /// </summary>
    public static IReadOnlyList<SmartDriveInfo> GetSmartDrives()
    {
        var list = new List<SmartDriveInfo>();
        try
        {
            var pnpMap = GetPnpDeviceIdToInstanceNameMap();
            var failureMap = GetFailurePredictStatusMap();
            var smartMap = GetSmartDataMap();

            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Index, Size, Model, PNPDeviceID FROM Win32_DiskDrive");

            foreach (ManagementBaseObject obj in searcher.Get())
            {
                var pnpId = obj["PNPDeviceID"]?.ToString();
                if (string.IsNullOrEmpty(pnpId)) continue;

                var instanceName = pnpMap.GetValueOrDefault(pnpId.ToUpperInvariant());
                if (string.IsNullOrEmpty(instanceName)) continue;

                var (predictFailure, reason) = failureMap.GetValueOrDefault(instanceName, (null, null));
                var vendorSpecific = smartMap.GetValueOrDefault(instanceName);

                var drive = new SmartDriveInfo
                {
                    Index = obj["Index"] != null ? Convert.ToInt32(obj["Index"]) : -1,
                    Model = obj["Model"]?.ToString()?.Trim() ?? "Unknown",
                    Size = obj["Size"] != null ? Convert.ToUInt64(obj["Size"]) : 0,
                    PNPDeviceID = pnpId,
                    PredictFailure = predictFailure,
                    Reason = reason,
                    Attributes = ParseSmartAttributes(vendorSpecific)
                };

                list.Add(drive);
            }
        }
        catch { /* Caller can handle empty list */ }

        return list.OrderBy(d => d.Index).ToList();
    }

    private static Dictionary<string, string> GetPnpDeviceIdToInstanceNameMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT PnPDeviceId, InstanceName FROM MSWmi_PnPDeviceId");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                var pnpId = obj["PnPDeviceId"]?.ToString();
                var instanceName = obj["InstanceName"]?.ToString();
                if (!string.IsNullOrEmpty(pnpId) && !string.IsNullOrEmpty(instanceName))
                    map[pnpId.TrimEnd(' ', '\0')] = instanceName.TrimEnd(' ', '\0');
            }
        }
        catch { /* Return empty map */ }
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
                if (string.IsNullOrEmpty(instanceName)) continue;

                bool? predictFailure = obj["PredictFailure"] != null ? Convert.ToBoolean(obj["PredictFailure"]) : null;
                uint? reason = obj["Reason"] != null ? Convert.ToUInt32(obj["Reason"]) : null;
                map[instanceName] = (predictFailure, reason);
            }
        }
        catch { /* Return empty map */ }
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
                if (!string.IsNullOrEmpty(instanceName) && data != null && data.Length >= 2 + AttributeSize)
                    map[instanceName] = data;
            }
        }
        catch { /* Return empty map */ }
        return map;
    }

    private static IReadOnlyList<SmartAttribute> ParseSmartAttributes(byte[]? vendorSpecific)
    {
        var list = new List<SmartAttribute>();
        if (vendorSpecific == null || vendorSpecific.Length < 2 + AttributeSize) return list;

        for (int offset = 2; offset <= vendorSpecific.Length - AttributeSize; offset += AttributeSize)
        {
            byte id = vendorSpecific[offset];
            if (id == 0) continue;

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
