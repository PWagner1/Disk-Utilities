# Drive-Wiper

A **disk formatting and partition tool** (Disk Eraser) with a WinUI 3 GUI. Format any drive (except `C:\`) with support for Windows (NTFS, FAT, exFAT, ReFS), Linux (ext2/3/4, Btrfs, XFS), and Apple (HFS, HFS+, APFS) file systems.

## Features

- **WinUI 3** desktop UI (Windows App SDK)
- **Many file systems**: Windows (NTFS, FAT32, FAT, exFAT, ReFS), Linux (ext2, ext3, ext4, Btrfs, XFS), Apple (HFS, HFS+, APFS). Windows natively formats only NTFS/FAT/exFAT/ReFS; others may show “Unsupported file system” unless you use third-party tools.
- **Secure deletion**: Optional multi-pass overwrite of free space before format (1 pass, DoD 3-pass, or Military grade 7-pass).
- **Safety**: The system drive (`C:\`) is never listed and cannot be formatted
- **Options**: Volume label, allocation unit size, quick format, compress files to save space (NTFS and supported file systems)

## Requirements

- Windows 10 (19041+) or Windows 11
- [Visual Studio 2022](https://visualstudio.microsoft.com/) with the **Windows application development** (WinUI) workload, or .NET 8 SDK for command-line build
- Build for a specific platform (e.g. **x64**); AnyCPU is not supported for this app

## Build and run

1. Open `Source/Disk Utilities.sln` in Visual Studio 2022.
2. Set platform to **x64** (or x86/ARM64).
3. Build and run (F5).

From command line (from repo root):

```powershell
cd Source
dotnet build "Disk Utilities.sln" -c Debug -p:Platform=x64
dotnet run --project DiskEraser --no-build
```

If the XamlCompiler step fails from the CLI, build and run from Visual Studio instead.

## Project layout

All source code is under **Source/**:

- **Source/DiskEraser/** – WinUI 3 app (Disk Eraser)
- **Source/DiskEraser/Models/** – `DriveItem` model
- **Source/DiskEraser/Services/** – `DriveService` (WMI drive list and format)
- **Source/README.md** – More detail on structure and packaging

## License

See [LICENSE](LICENSE).