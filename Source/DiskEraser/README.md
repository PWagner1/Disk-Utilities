# DiskEraser

WinUI 3 tool for **formatting volumes** and **managing partitions**. Part of the [Disk Utilities](../../README.md) suite.

## Features

- List formattable volumes (system / boot / EFI / recovery volumes excluded)
- Format with volume label, allocation unit size, quick format, and NTFS compression where supported
- Last-used format options (FS, label, allocation unit, quick format, overwrite preset) are restored on launch
- Optional **free-space overwrite** before format (1 / 3 / 7 pass presets) with pass % and ETA
- Separate wipe vs format progress in the status line
- Partition layout diagram with configurable colors (Settings)
- Partition management: create, delete, extend, shrink (via diskpart); failures show diskpart output; shrink/extend show a before/after size preview
- Advanced (partition window): probe ATA/NVMe hardware-erase capabilities; NVMe Sanitize when advertised (gated, admin-only, protected disks refused). ATA Secure Erase is detected but not automated

## File systems

The UI lists Windows (NTFS, FAT32, FAT, exFAT, ReFS) as **selectable**, and Linux/Apple options as **disabled** (not supported via stock Windows WMI format).

Formatting uses WMI `Win32_Volume.Format`. Linux and Apple choices need third-party tools outside this app.

## Free-space overwrite (what it actually does)

When enabled, DiskEraser overwrites **free space** on the selected volume with random data for the chosen number of passes, then formats. It does **not** perform full-disk overwrite of allocated data without format.

**Hardware erase** (Partition Management → Advanced) is separate: it probes ATA Secure Erase / NVMe Sanitize capabilities. NVMe Sanitize may be submitted when the controller advertises it; ATA Secure Erase is reported but not automated (passworded UNIT). Disks with protected volumes are refused. This is still **not** a DoD/military certification; remapped SSD cells may retain data.

Pass presets for free-space overwrite describe **effort** only; they are not a standards certification claim.

## Protected volumes

These cannot be formatted or wiped:

- Windows system volume (`C:`)
- Volumes marked boot or system in WMI
- Labels that look like EFI / ESP / Recovery / System Reserved / WinRE
- Typical letterless EFI-sized FAT partitions on the diagram

## Requirements

- Windows 10 (19041+) or Windows 11
- .NET 10 / Windows App SDK 2.x (see solution projects)
- Elevation recommended for format and partition operations
- Platform: x64, x86, or ARM64 (not AnyCPU)

## Build and run

```powershell
cd Source
dotnet build DiskEraser\DiskEraser.csproj -c Debug -p:Platform=x64
dotnet run --project DiskEraser -p:Platform=x64
```

Or open `Source/Disk Utilities.sln`, set **DiskEraser** as startup, platform **x64**, and press F5.

Executable output (repo root):

`bin\x64\Debug\net10.0-windows10.0.19041.0\DiskEraser\DiskEraser.exe`

## Safety

- Never formats protected system/boot/EFI/recovery volumes
- Format and wipe are destructive — confirm the correct volume
- Prefer quick format only when you do not need free-space overwrite
- Partition shrink/extend can fail if the volume is in use or the FS refuses the size

## Related

- Suite overview: [Source/README.md](../README.md)
- Improvement tracking: [IMPROVEMENTS.md](../../IMPROVEMENTS.md)
