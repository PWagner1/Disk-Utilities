# DiskHealth

WinUI 3 tool for **signature-based file recovery** (carving) and **S.M.A.R.T. drive health**. Part of the [Disk Utilities](../../README.md) suite.

## Features

- Carve a volume for known file signatures
- List candidates with type and a **heuristic** recovery chance estimate
- Recover selected files to a folder you choose
- **S.M.A.R.T.** window: failure prediction and attribute tables (strongest on ATA/SATA)

## How recovery works (expectations)

DiskHealth searches raw volume data for magic bytes / headers, for example:

JPEG, PNG, PDF, GIF, ZIP, Office Open XML, BMP, MP3, MP4 (ftyp), WAV

This is **carving**, not NTFS MFT or Recycle Bin undelete. Results may include false positives; “chance” scores are heuristics, not guarantees. Fragmented or overwritten files may not recover cleanly.

Admin rights are typically required to open the volume for raw scanning.

## S.M.A.R.T. notes

- Uses WMI (`MSStorageDriver_FailurePredictStatus`, `MSStorageDriver_ATAPISmartData`, etc.)
- Detailed attribute tables apply mainly to ATA/SATA
- NVMe drives often show health / failure-predict status only

## Requirements

- Windows 10 (19041+) or Windows 11
- .NET 10 / Windows App SDK 2.x
- Administrator recommended for scan/recover
- Platform: x64, x86, or ARM64 (not AnyCPU)

## Build and run

```powershell
cd Source
dotnet build DiskHealth\DiskHealth.csproj -c Debug -p:Platform=x64
dotnet run --project DiskHealth -p:Platform=x64
```

Or open `Source/Disk Utilities.sln`, set **DiskHealth** as startup, platform **x64**, and press F5.

Executable output (repo root):

`bin\x64\Debug\net10.0-windows10.0.19041.0\DiskHealth.exe`

## Usage tips

1. Prefer a healthy destination drive for recovered files (not the same volume you are scanning when possible).
2. Stop writing to a failing or accidentally wiped volume before scanning.
3. Review candidates and recover selectively; carving can produce many hits.
4. Use the S.M.A.R.T. window to check drive health separately from recovery.

## Safety

- Recovery reads raw disks; run as Administrator when prompted ops fail
- Do not treat this as a substitute for backups or professional forensics
- S.M.A.R.T. warnings are advisory — back up and replace drives that predict failure

## Related

- Suite overview: [Source/README.md](../README.md)
- Improvement tracking: [IMPROVEMENTS.md](../../IMPROVEMENTS.md)
