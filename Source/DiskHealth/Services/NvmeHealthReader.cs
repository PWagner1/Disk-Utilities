using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiskHealth.Services;

/// <summary>
/// Reads NVMe SMART/Health Information Log (Log Identifier 02h) via IOCTL_STORAGE_QUERY_PROPERTY.
/// </summary>
public static class NvmeHealthReader
{
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const int StorageDeviceProtocolSpecificProperty = 50;
    private const int PropertyStandardQuery = 0;
    private const int ProtocolTypeNvme = 3;
    private const int NvmeDataTypeLogPage = 2;
    private const int NvmeLogSmartHealth = 0x02;
    private const int FileFlagNoBuffering = 0x20000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery
    {
        public int PropertyId;
        public int QueryType;
        // Followed by STORAGE_PROTOCOL_SPECIFIC_DATA + protocol buffer
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StorageProtocolSpecificData
    {
        public int ProtocolType;
        public int DataType;
        public int ProtocolDataRequestValue;
        public int ProtocolDataRequestSubValue;
        public int ProtocolDataOffset;
        public int ProtocolDataLength;
        public uint FixedProtocolReturnData;
        public int ProtocolDataRequestSubValue2;
        public int ProtocolDataRequestSubValue3;
        public int ProtocolDataRequestSubValue4;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        int nInBufferSize,
        byte[] lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    public static bool IsLikelyNvme(string? interfaceType, string? pnpDeviceId, string? model)
    {
        if (!string.IsNullOrEmpty(interfaceType) &&
            interfaceType.Contains("SCSI", StringComparison.OrdinalIgnoreCase) &&
            (pnpDeviceId?.Contains("NVME", StringComparison.OrdinalIgnoreCase) == true
             || model?.Contains("NVMe", StringComparison.OrdinalIgnoreCase) == true))
        {
            return true;
        }

        return pnpDeviceId?.Contains("NVME", StringComparison.OrdinalIgnoreCase) == true
               || model?.Contains("NVMe", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static NvmeHealthInfo? TryRead(int diskIndex)
    {
        string path = $"\\\\.\\PhysicalDrive{diskIndex}";
        using SafeFileHandle handle = CreateFile(
            path,
            0, // query access
            0x00000003, // read|write share
            IntPtr.Zero,
            3, // OPEN_EXISTING
            FileFlagNoBuffering,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return null;
        }

        const int logSize = 512;
        int headerSize = Marshal.SizeOf<StoragePropertyQuery>() + Marshal.SizeOf<StorageProtocolSpecificData>();
        int bufferSize = headerSize + logSize;
        byte[] buffer = new byte[bufferSize];

        // Property query
        BitConverter.GetBytes(StorageDeviceProtocolSpecificProperty).CopyTo(buffer, 0);
        BitConverter.GetBytes(PropertyStandardQuery).CopyTo(buffer, 4);

        var specific = new StorageProtocolSpecificData
        {
            ProtocolType = ProtocolTypeNvme,
            DataType = NvmeDataTypeLogPage,
            ProtocolDataRequestValue = NvmeLogSmartHealth,
            ProtocolDataRequestSubValue = 0,
            ProtocolDataOffset = Marshal.SizeOf<StorageProtocolSpecificData>(),
            ProtocolDataLength = logSize
        };

        int specificOffset = Marshal.SizeOf<StoragePropertyQuery>();
        IntPtr tmp = Marshal.AllocHGlobal(Marshal.SizeOf<StorageProtocolSpecificData>());
        try
        {
            Marshal.StructureToPtr(specific, tmp, false);
            Marshal.Copy(tmp, buffer, specificOffset, Marshal.SizeOf<StorageProtocolSpecificData>());
        }
        finally
        {
            Marshal.FreeHGlobal(tmp);
        }

        if (!DeviceIoControl(handle, IoctlStorageQueryProperty, buffer, buffer.Length, buffer, buffer.Length, out int returned, IntPtr.Zero)
            || returned < headerSize + 32)
        {
            return null;
        }

        int dataOffset = specificOffset + specific.ProtocolDataOffset;
        if (dataOffset + 32 > buffer.Length)
        {
            return null;
        }

        return ParseSmartLog(buffer.AsSpan(dataOffset, Math.Min(logSize, buffer.Length - dataOffset)));
    }

    /// <summary>Parses NVMe SMART / Health Information Log bytes (for tests).</summary>
    public static NvmeHealthInfo ParseSmartLog(ReadOnlySpan<byte> log)
    {
        if (log.Length < 32)
        {
            return new NvmeHealthInfo();
        }

        // Temperature is Kelvin in bytes 1-2 (little-endian).
        int kelvin = log[1] | (log[2] << 8);
        int celsius = kelvin > 0 ? kelvin - 273 : 0;

        return new NvmeHealthInfo
        {
            CriticalWarning = log[0],
            TemperatureCelsius = celsius,
            AvailableSparePercent = log[3],
            AvailableSpareThreshold = log[4],
            PercentageUsed = log[5],
            DataUnitsRead = ReadUInt128Low64(log, 32),
            DataUnitsWritten = ReadUInt128Low64(log, 48),
            PowerCycles = ReadUInt128Low64(log, 112),
            PowerOnHours = ReadUInt128Low64(log, 128),
            UnsafeShutdowns = ReadUInt128Low64(log, 144),
            MediaErrors = ReadUInt128Low64(log, 160)
        };
    }

    private static ulong ReadUInt128Low64(ReadOnlySpan<byte> log, int offset)
    {
        if (offset + 8 > log.Length)
        {
            return 0;
        }

        ulong v = 0;
        for (int i = 0; i < 8; i++)
        {
            v |= (ulong)log[offset + i] << (8 * i);
        }

        return v;
    }
}
