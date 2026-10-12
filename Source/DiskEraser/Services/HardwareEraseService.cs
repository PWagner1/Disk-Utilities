using System.Runtime.InteropServices;
using DiskEraser.Models;
using Microsoft.Win32.SafeHandles;

namespace DiskEraser.Services;

/// <summary>
/// Probes ATA/NVMe hardware erase capabilities and optionally issues NVMe Sanitize / ATA Secure Erase.
/// Always refuse disks that contain protected volumes. Not a standards certification.
/// </summary>
public static class HardwareEraseService
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagNoBuffering = 0x20000000;

    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint IoctlAtaPassThrough = 0x0004D02C;
    private const uint IoctlStorageProtocolCommand = 0x002D51C0;

    private const int StorageDeviceProtocolSpecificProperty = 50;
    private const int PropertyStandardQuery = 0;
    private const int ProtocolTypeNvme = 3;
    private const int ProtocolTypeAta = 2;
    private const int NvmeDataTypeIdentify = 1;
    private const int NvmeIdentifyController = 1;

    // NVMe Admin Sanitize
    private const byte NvmeAdminOpcodeSanitize = 0x84;
    private const byte NvmeSanitizeActionCryptoErase = 4;
    private const byte NvmeSanitizeActionBlockErase = 2;
    private const byte NvmeSanitizeActionOverwrite = 3;

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

    /// <summary>Queries hardware erase capabilities for a physical disk index.</summary>
    public static HardwareEraseCapability QueryCapabilities(int diskIndex)
    {
        string model = string.Empty;
        string? interfaceType = null;
        string? pnp = null;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT Model, InterfaceType, PNPDeviceID FROM Win32_DiskDrive WHERE Index = {diskIndex}");
            foreach (var o in searcher.Get())
            {
                using var disk = (ManagementObject)o;
                model = disk["Model"]?.ToString()?.Trim() ?? string.Empty;
                interfaceType = disk["InterfaceType"]?.ToString();
                pnp = disk["PNPDeviceID"]?.ToString();
                break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warning($"WMI disk lookup failed for disk {diskIndex}.", ex, nameof(HardwareEraseService));
        }

        bool likelyNvme = IsLikelyNvme(interfaceType, pnp, model);
        if (likelyNvme)
        {
            var nvme = TryQueryNvmeSanitize(diskIndex);
            return new HardwareEraseCapability
            {
                DiskIndex = diskIndex,
                Model = model,
                Bus = HardwareEraseBus.Nvme,
                NvmeSanitizeCryptoErase = nvme.crypto,
                NvmeSanitizeBlockErase = nvme.block,
                NvmeSanitizeOverwrite = nvme.overwrite
            };
        }

        var ata = TryQueryAtaSecurity(diskIndex);
        return new HardwareEraseCapability
        {
            DiskIndex = diskIndex,
            Model = model,
            Bus = HardwareEraseBus.Ata,
            AtaSecureEraseSupported = ata.supported,
            AtaSecurityFrozen = ata.frozen
        };
    }

    /// <summary>
    /// Attempts hardware erase. Refuses without admin, without capability, or if the disk hosts protected volumes.
    /// </summary>
    public static async Task<(bool Ok, string Message)> TryHardwareEraseAsync(
        int diskIndex,
        HardwareEraseCapability capability,
        CancellationToken cancellationToken = default)
    {
        if (!ElevationHelper.IsElevated)
        {
            return (false, "Administrator rights are required for hardware erase.");
        }

        if (!capability.CanAttemptHardwareErase)
        {
            return (false, "This disk does not advertise an available hardware erase method (or ATA security is frozen).");
        }

        if (DiskContainsProtectedVolume(diskIndex))
        {
            return (false, "Refused: this disk contains a protected system/boot/EFI/recovery volume.");
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (capability.Bus == HardwareEraseBus.Nvme)
            {
                return TryNvmeSanitize(diskIndex, capability);
            }

            return TryAtaSecureErase(diskIndex, capability);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>True if any partition on the disk is protected (must never hardware-erase).</summary>
    public static bool DiskContainsProtectedVolume(int diskIndex)
    {
        try
        {
            var disks = DriveService.GetDiskPartitionLayoutAsync().GetAwaiter().GetResult();
            var disk = disks.FirstOrDefault(d => d.DiskIndex == diskIndex);
            return disk?.Partitions.Any(p => p.IsProtected) == true;
        }
        catch (Exception ex)
        {
            AppLog.Warning($"Could not verify protected volumes on disk {diskIndex}.", ex, nameof(HardwareEraseService));
            return true; // fail closed
        }
    }

    /// <summary>Parses NVMe Identify Controller SANICAP dword (offset 328) for tests.</summary>
    public static (bool crypto, bool block, bool overwrite) ParseNvmeSanicap(uint sanicap)
    {
        // NVMe SANICAP: bit 0 overwrite, bit 1 block erase, bit 2 crypto erase (see NVMe Base Spec)
        bool overwrite = (sanicap & 0x1) != 0;
        bool block = (sanicap & 0x2) != 0;
        bool crypto = (sanicap & 0x4) != 0;
        return (crypto, block, overwrite);
    }

    /// <summary>Parses ATA IDENTIFY words 82/85/128 for security erase / frozen (for tests).</summary>
    public static (bool supported, bool frozen) ParseAtaSecurityWords(ushort word82, ushort word85, ushort word128)
    {
        // Word 82 bit 1: Security feature set supported; Word 85 bit 1: enabled; Word 128 bit 3: frozen
        bool supported = (word82 & 0x0002) != 0 || (word85 & 0x0002) != 0;
        bool frozen = (word128 & 0x0008) != 0;
        return (supported, frozen);
    }

    private static bool IsLikelyNvme(string? interfaceType, string? pnpDeviceId, string? model)
        => pnpDeviceId?.Contains("NVME", StringComparison.OrdinalIgnoreCase) == true
           || model?.Contains("NVMe", StringComparison.OrdinalIgnoreCase) == true
           || (interfaceType?.Contains("SCSI", StringComparison.OrdinalIgnoreCase) == true
               && (pnpDeviceId?.Contains("NVME", StringComparison.OrdinalIgnoreCase) == true
                   || model?.Contains("NVMe", StringComparison.OrdinalIgnoreCase) == true));

    private static (bool crypto, bool block, bool overwrite) TryQueryNvmeSanitize(int diskIndex)
    {
        using SafeFileHandle? handle = OpenPhysicalDrive(diskIndex, write: false);
        if (handle == null)
        {
            return default;
        }

        // Identify Controller is 4096 bytes
        const int identifySize = 4096;
        int headerSize = 8 + 40; // STORAGE_PROPERTY_QUERY + STORAGE_PROTOCOL_SPECIFIC_DATA
        byte[] buffer = new byte[headerSize + identifySize];
        BitConverter.GetBytes(StorageDeviceProtocolSpecificProperty).CopyTo(buffer, 0);
        BitConverter.GetBytes(PropertyStandardQuery).CopyTo(buffer, 4);

        // STORAGE_PROTOCOL_SPECIFIC_DATA
        int o = 8;
        BitConverter.GetBytes(ProtocolTypeNvme).CopyTo(buffer, o); o += 4;
        BitConverter.GetBytes(NvmeDataTypeIdentify).CopyTo(buffer, o); o += 4;
        BitConverter.GetBytes(NvmeIdentifyController).CopyTo(buffer, o); o += 4; // request value
        BitConverter.GetBytes(0).CopyTo(buffer, o); o += 4; // subvalue
        BitConverter.GetBytes(40).CopyTo(buffer, o); o += 4; // offset of data from start of specific struct
        BitConverter.GetBytes(identifySize).CopyTo(buffer, o);

        if (!DeviceIoControl(handle, IoctlStorageQueryProperty, buffer, buffer.Length, buffer, buffer.Length, out int returned, IntPtr.Zero)
            || returned < headerSize + 332)
        {
            AppLog.Warning($"NVMe Identify query failed for disk {diskIndex} (Win32 {Marshal.GetLastWin32Error()}).",
                source: nameof(HardwareEraseService));
            return default;
        }

        int dataOffset = 8 + 40;
        if (dataOffset + 332 > buffer.Length)
        {
            return default;
        }

        uint sanicap = BitConverter.ToUInt32(buffer, dataOffset + 328);
        return ParseNvmeSanicap(sanicap);
    }

    private static (bool supported, bool frozen) TryQueryAtaSecurity(int diskIndex)
    {
        using SafeFileHandle? handle = OpenPhysicalDrive(diskIndex, write: false);
        if (handle == null)
        {
            return default;
        }

        // Minimal ATA PASS THROUGH Identify Device (0xEC) — 512-byte data
        // ATA_PASS_THROUGH_EX is 40 bytes + 512
        const int aptSize = 40;
        const int dataSize = 512;
        byte[] buffer = new byte[aptSize + dataSize];
        // Length
        BitConverter.GetBytes((ushort)aptSize).CopyTo(buffer, 0);
        // AtaFlags: ATA_FLAGS_DATA_IN (0x02)
        BitConverter.GetBytes((ushort)0x02).CopyTo(buffer, 2);
        buffer[6] = 1; // AtaFlags high / reserved layout varies — set PathId etc carefully
        // TimeOutValue at offset 8
        BitConverter.GetBytes(10u).CopyTo(buffer, 8);
        // DataBufferOffset = 40
        BitConverter.GetBytes((int)aptSize).CopyTo(buffer, 16);
        // LengthOfTransfer = 512
        BitConverter.GetBytes((uint)dataSize).CopyTo(buffer, 20);
        // PreviousTaskFile[8] + CurrentTaskFile[8] at 24
        // CurrentTaskFile: command at byte 30 (offset 24+6) = 0xEC Identify
        buffer[24 + 6] = 0xEC;

        if (!DeviceIoControl(handle, IoctlAtaPassThrough, buffer, buffer.Length, buffer, buffer.Length, out int returned, IntPtr.Zero)
            || returned < aptSize + 256)
        {
            // Fallback: treat as unsupported rather than guessing
            AppLog.Info($"ATA Identify pass-through unavailable for disk {diskIndex} (Win32 {Marshal.GetLastWin32Error()}).",
                source: nameof(HardwareEraseService));
            return default;
        }

        // IDENTIFY data is little-endian words
        ushort Word(int index) => BitConverter.ToUInt16(buffer, aptSize + index * 2);
        return ParseAtaSecurityWords(Word(82), Word(85), Word(128));
    }

    private static (bool Ok, string Message) TryNvmeSanitize(int diskIndex, HardwareEraseCapability capability)
    {
        byte action;
        string actionName;
        if (capability.NvmeSanitizeCryptoErase)
        {
            action = NvmeSanitizeActionCryptoErase;
            actionName = "Crypto Erase";
        }
        else if (capability.NvmeSanitizeBlockErase)
        {
            action = NvmeSanitizeActionBlockErase;
            actionName = "Block Erase";
        }
        else if (capability.NvmeSanitizeOverwrite)
        {
            action = NvmeSanitizeActionOverwrite;
            actionName = "Overwrite";
        }
        else
        {
            return (false, "No NVMe Sanitize action available.");
        }

        using SafeFileHandle? handle = OpenPhysicalDrive(diskIndex, write: true);
        if (handle == null)
        {
            return (false, "Could not open physical disk for write (admin required).");
        }

        // STORAGE_PROTOCOL_COMMAND + 64-byte NVMe command
        const int cmdStructSize = 76; // up to Command[0]
        const int nvmeCmdLen = 64;
        byte[] buffer = new byte[cmdStructSize + nvmeCmdLen];
        BitConverter.GetBytes(1u).CopyTo(buffer, 0); // Version
        BitConverter.GetBytes((uint)(cmdStructSize + nvmeCmdLen)).CopyTo(buffer, 4); // Length
        BitConverter.GetBytes(ProtocolTypeNvme).CopyTo(buffer, 8);
        BitConverter.GetBytes(0x01u).CopyTo(buffer, 12); // STORAGE_PROTOCOL_COMMAND_FLAG_ADAPTER_REQUEST
        BitConverter.GetBytes((uint)nvmeCmdLen).CopyTo(buffer, 24); // CommandLength
        BitConverter.GetBytes(120u).CopyTo(buffer, 36); // TimeOutValue seconds

        int cmdOffset = cmdStructSize;
        buffer[cmdOffset + 0] = NvmeAdminOpcodeSanitize;
        // DW10: SANACT in bits 2:0 — NVMe places SANACT in CDW10
        buffer[cmdOffset + 40] = action;

        if (!DeviceIoControl(handle, IoctlStorageProtocolCommand, buffer, buffer.Length, buffer, buffer.Length, out _, IntPtr.Zero))
        {
            int err = Marshal.GetLastWin32Error();
            AppLog.Error($"NVMe Sanitize ({actionName}) failed for disk {diskIndex} (Win32 {err}).",
                source: nameof(HardwareEraseService));
            return (false, $"NVMe Sanitize ({actionName}) failed (Win32 {err}). The controller may require vendor tools, or the command is blocked.");
        }

        AppLog.Info($"NVMe Sanitize ({actionName}) submitted for disk {diskIndex}.", source: nameof(HardwareEraseService));
        return (true, $"NVMe Sanitize ({actionName}) was submitted for Disk {diskIndex}. The drive may be busy for a long time; do not power off. Remapped cells may retain data — this is not a certification.");
    }

    private static (bool Ok, string Message) TryAtaSecureErase(int diskIndex, HardwareEraseCapability capability)
    {
        if (capability.AtaSecurityFrozen)
        {
            return (false, "ATA security is frozen. A power cycle (or suspend/resume on some systems) may clear the freeze lock; vendor tools may be required.");
        }

        // Issuing SECURITY ERASE UNIT requires setting a user password first — refuse rather than invent credentials.
        AppLog.Warning(
            $"ATA Secure Erase supported on disk {diskIndex} but passworded erase UNIT is not automated (refuse to set drive passwords).",
            source: nameof(HardwareEraseService));
        return (false,
            "ATA Secure Erase is detected but not automated here (requires setting a temporary security password). Use the drive vendor’s Secure Erase tool, or free-space overwrite + format in DiskEraser.");
    }

    private static SafeFileHandle? OpenPhysicalDrive(int diskIndex, bool write)
    {
        string path = $@"\\.\PhysicalDrive{diskIndex}";
        uint access = write ? GenericRead | GenericWrite : 0;
        var handle = CreateFile(path, access, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, FileFlagNoBuffering, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            AppLog.Warning($"CreateFile failed for {path} (Win32 {Marshal.GetLastWin32Error()}).",
                source: nameof(HardwareEraseService));
            return null;
        }

        return handle;
    }
}
