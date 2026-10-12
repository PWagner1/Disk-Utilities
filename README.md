# Disk Utilities

A suite of **WinUI 3** desktop tools for disk management on Windows 10 (19041+) and Windows 11.

| App | Purpose |
|-----|---------|
| **[SuiteLauncher](Source/SuiteLauncher/README.md)** | Launch the suite tools from one window (theme seed + elevation hints) |
| **[DiskEraser](Source/DiskEraser/README.md)** | Format volumes and manage partitions (system drive `C:\` excluded from format) |
| **[DiskHealth](Source/DiskHealth/README.md)** | Signature-based file recovery and S.M.A.R.T. drive health |
| **[DiskSeer](Source/README.md#diskseer--foreign-file-systems)** | Browse and edit Apple/Linux file systems from Windows |
| **[DriveStat](Source/DriveStat/README.md)** | Disk usage viewer (tree, treemap, extension stats) |
| **[HandleFinder](Source/HandleFinder/README.md)** | Find which processes lock files, folders, or drives (GUI + CLI) |

More detail on layout, packaging, and per-app behavior is in [Source/README.md](Source/README.md). Suggested improvements are tracked in [IMPROVEMENTS.md](IMPROVEMENTS.md); see [CHANGELOG.md](CHANGELOG.md) for release notes.

## Requirements

- Windows 10 (19041+) or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) and/or [Visual Studio 2022](https://visualstudio.microsoft.com/) with the **Windows application development** (WinUI) workload
- Windows App SDK **2.x** (restored via NuGet; apps are self-contained)
- Build for a specific platform (**x64**, x86, or ARM64); AnyCPU is not supported
- **Administrator** rights for DiskSeer (raw disk access) and HandleFinder (handle enumeration); some DiskEraser / DiskHealth operations also need elevation. Apps that run as `asInvoker` show a banner and can **Restart as administrator** via UAC when needed.

## Build and run

1. Open `Source/Disk Utilities.sln` in Visual Studio 2022.
2. Set platform to **x64** (or x86/ARM64).
3. Set a startup project and run (F5).

From the command line (from repo root):

```powershell
cd Source
dotnet build "Disk Utilities.sln" -c Debug -p:Platform=x64
dotnet run --project SuiteLauncher -p:Platform=x64
# or DiskEraser / DiskHealth / DiskSeer / DriveStat / HandleFinder
```

Binaries are written under `bin\<platform>\<config>\net10.0-windows10.0.19041.0\` at the repo root.

Tagged releases (`v*`) build a x64 zip (+ `SHA256SUMS.txt`) via `.github/workflows/release.yml`.  
winget / Scoop / Chocolatey / optional Inno templates live in [`packaging/`](packaging/README.md).

If the XamlCompiler step fails from the CLI, build and run from Visual Studio instead.

## Safety and expectations

These tools can destroy or alter data. Read the per-app notes before using destructive features.

- **DiskEraser** never formats protected volumes (C:, boot/system, EFI/recovery). Free-space overwrite fills **unused space** before format; it is not full-disk Secure Erase or NVMe Sanitize. Linux/Apple options appear disabled — stock Windows cannot format them via WMI.
- **DiskHealth** recovery is **signature carving** (searching raw data for known file headers), not full NTFS MFT undelete. Recovery “chance” values are heuristics. S.M.A.R.T. attribute tables are strongest for ATA/SATA; NVMe often shows failure-predict status only.
- **DiskSeer** requires admin and explicit partition selection. APFS browsing needs optional [libfsapfs](Source/DiskSeer/Docs/LIBFSAPFS_BUILD.md) (read-only).
- **HandleFinder** can close handles or end processes; prefer ending the locking app when possible. Protected processes may be inaccessible.
- **DriveStat** is read-only for analysis (open in Explorer); treat large scans as I/O-heavy.

See also the [Safety](Source/README.md#safety) section in the source overview.

## License

See [LICENSE](LICENSE).
