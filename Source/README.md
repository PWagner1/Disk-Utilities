# Disk Utilities – Source

This folder contains WinUI 3 desktop applications for disk management, plus a shared library:

- **[SuiteLauncher](SuiteLauncher/README.md)** – Suite entry point (launch tools, theme seed)
- **[DiskEraser](DiskEraser/README.md)** – Format drives with optional free-space overwrite; partition create/delete/extend/shrink
- **[DiskHealth](DiskHealth/README.md)** – Signature-based file recovery and S.M.A.R.T. drive health
- **DiskSeer** – Read and modify drives formatted with Apple or Linux file systems
- **[DriveStat](DriveStat/README.md)** – Disk usage statistics (tree, treemap, extensions)
- **[HandleFinder](HandleFinder/README.md)** – Find processes locking files, folders, or drives
- **DiskUtilities.Shared** – Shared helpers (elevation, logging, suite launch/settings, confirmations)

## Structure

- **DiskUtilities.Shared/** – Class library referenced by all apps (`ElevationHelper`, `ElevationUi`, `WindowChrome`, `ByteFormatter`, `AppLog`, `SuiteApps`, `SuiteSettings`)
- **Directory.Build.props** / **Directory.Build.targets** – Shared TFM, package versions, and suite `bin\` output for apps

- **SuiteLauncher/** – WinUI 3 suite entry point
  - **MainWindow.xaml** – Tool list, theme seed, launch / launch elevated
  - **README.md** – Features and build notes

- **DiskEraser/** – WinUI 3 project for drive formatting
  - **MainWindow.xaml** – Drive selection, format options, partition layout diagram
  - **PartitionManagementWindow.xaml** – Create, delete, extend, and shrink partitions
  - **Models/** – DiskInfo, DriveItem, PartitionInfo models
  - **Services/DriveService.cs** – WMI-based drive enumeration, formatting, and diskpart operations
  - **README.md** – Features, build, safety notes

- **DiskHealth/** – WinUI 3 project for file recovery and health
  - **MainWindow.xaml** – Drive selection, scan, recoverable file list with recovery chances
  - **SmartStatsWindow.xaml** – S.M.A.R.T. attributes UI
  - **Models/** – RecoverableFileInfo, ScannableDrive, SmartDriveInfo, SmartAttribute
  - **Services/RecoveryService.cs** – Signature scanning and recovery
  - **Services/SmartService.cs** – WMI S.M.A.R.T. / failure predict
  - **README.md** – Features, build, expectations

- **DiskSeer/** – WinUI 3 project for accessing foreign file systems
  - **MainWindow.xaml** – Drive/partition selection, file browser with breadcrumb navigation
  - **Models/** – ForeignDriveInfo, ForeignPartitionInfo, FileSystemEntry, BreadcrumbItem
  - **Services/ForeignDriveService.cs** – Detection and access via DiscUtils (+ optional APFS)
  - **Docs/LIBFSAPFS_BUILD.md** – Building/placing libfsapfs for APFS

- **DriveStat/** – WinUI 3 disk usage viewer
  - **MainWindow.xaml** – Tree, treemap, extension list
  - **Controls/TreemapControl** – Squarified treemap
  - **Services/ScanService.cs** – Recursive size scan
  - **README.md** – Features and usage

- **HandleFinder/** – WinUI 3 project for finding file locks
  - **MainWindow.xaml** – Path input, drive selector, results list, end process
  - **Models/** – HandleInfo, DriveItem
  - **Services/HandleService.cs** – Handle enumeration via NtQuerySystemInformation
  - **CliRunner.cs** – Command-line mode
  - **README.md** – Comprehensive documentation

## Requirements

- Windows 10 19041+ or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Visual Studio 2022 with **Windows application development** (WinUI) workload, or build from command line with `dotnet build`
- Windows App SDK **2.x** (NuGet; self-contained)
- **Administrator rights** – Required for DiskSeer (raw disk access) and HandleFinder (handle enumeration)

## Build and run

From the repo root or from `Source`:

```powershell
cd Source
dotnet build "Disk Utilities.sln" -c Debug -p:Platform=x64
dotnet run --project DiskEraser -p:Platform=x64
# or
dotnet run --project DiskHealth -p:Platform=x64
dotnet run --project DiskSeer -p:Platform=x64
dotnet run --project DriveStat -p:Platform=x64
dotnet run --project HandleFinder -p:Platform=x64
```

Or open `Source/Disk Utilities.sln` in Visual Studio and press F5.

Output binaries land under the repo-root `bin\` folder (see each project’s `BaseOutputPath`), for example:

`bin\x64\Debug\net10.0-windows10.0.19041.0\<AppName>.exe`

## DiskEraser – Free-space overwrite

When **Overwrite free space before format** is enabled, the app fills unused space on the selected volume with cryptographically random data for the chosen number of passes, then formats. This is **not** full-disk Secure Erase or NVMe Sanitize, and preset names are not a standards certification.

Presets (effort only):

- **1 pass** — free-space overwrite
- **3 passes** — free-space overwrite (higher effort)
- **7 passes** — free-space overwrite (highest effort)

Protected volumes (C:, boot/system WMI flags, EFI/recovery labels, typical ESP partitions) cannot be formatted. See [DiskEraser/README.md](DiskEraser/README.md).

Linux/Apple file systems appear in the format list but are **disabled** on stock Windows.

## DiskSeer – Foreign File Systems

DiskSeer can read and write drives formatted with non-Windows file systems:

**Supported file systems (read/write via DiscUtils):**

- **Linux**: ext2, ext3, ext4, Btrfs, XFS
- **Apple**: HFS+ (read/write)

**APFS (optional):**

- Detected always; partitions show `[needs libfsapfs]` until the native library is present
- **Browsing** requires `libfsapfs-1.dll` (+ deps) next to `DiskSeer.exe` — see [LIBFSAPFS_BUILD.md](DiskSeer/Docs/LIBFSAPFS_BUILD.md) (also shipped under `Docs\` in the output folder; DiskSeer has an in-app “Open build docs” / “Show DLL folder” banner)
- Read-only; APFS v2; no T2 encryption / LZFSE / snapshots
- Opening stages the partition to a temp file (progress in UI) up to **128 GB** for libfsapfs

### Features

- Browse files and folders on foreign file systems
- Copy files from foreign drives to Windows
- Copy files from Windows to foreign drives
- Create, rename, and delete files and folders
- View file properties (permissions, timestamps, sizes)

### How it works

DiskSeer uses the [DiscUtils](https://github.com/DiscUtils/DiscUtils) library to parse file system structures directly from raw disk sectors. This requires administrator privileges to access `\\.\PhysicalDriveN` devices.

### Multicore and multithreading

- **Drive enumeration**: When you refresh drives, each physical disk is scanned on a separate thread-pool thread (parallelism capped by core count, max 8) so multiple drives are detected concurrently.
- **Batch file copy**: Copying multiple files to or from a foreign drive runs up to 2 copy operations concurrently (configurable) to use multiple cores without overwhelming a single disk.
- **UI**: Long-running work (drive scan, directory load, copy) runs on background threads; progress and results are marshalled to the UI thread so the window stays responsive.

## HandleFinder – File Lock Discovery

HandleFinder identifies which processes have open handles (file locks) on a given file, folder, or drive. It helps resolve "Access Denied" and "File in Use" errors.

- **Drive support**: Search entire volumes (e.g., `C:\`)
- **Path types**: Files, folders, or drives
- **End process / close handle**: Optional actions with confirmation
- **CLI**: `--json`, `--kill`, `--wait` for automation

See **[HandleFinder/README.md](HandleFinder/README.md)** for full documentation, API reference, and troubleshooting.

## File systems (DiskEraser)

The format UI offers:

- **Windows**: NTFS, FAT32, FAT, exFAT, ReFS
- **Linux**: ext2, ext3, ext4, Btrfs, XFS
- **Apple**: HFS, HFS+, APFS

Formatting is done via WMI `Win32_Volume.Format`. Windows natively supports NTFS, FAT, and FAT32; exFAT and ReFS may work on newer Windows. Linux and Apple file systems typically return "Unsupported file system" unless you use third-party drivers.

## Safety

- Protected volumes (C:, boot/system, EFI/recovery) are never listed for formatting in DiskEraser
- DiskEraser free-space overwrite is unused-space fill before format only — not hardware erase
- DiskHealth recovery is signature carving; “chance” scores are heuristics — not a guarantee and not MFT undelete
- DiskSeer requires explicit partition selection before any operations; prefer read-only exploration on unfamiliar disks
- HandleFinder requires Administrator; closing handles or killing processes can crash applications or lose data
- Always back up important data before format, wipe, partition, or recovery experiments

## Packaging

Projects are configured as **unpackaged** (`WindowsPackageType=None`) for simple build and run. Distribution is via GitHub Releases zip plus winget/Scoop/Chocolatey templates under [`packaging/`](../packaging/README.md). Full MSIX remains deferred (needs Assets + signing); do not add incomplete manifests.

## Tests

```powershell
cd Source
dotnet test DiskUtilities.Tests -c Debug -p:Platform=x64 -m:1
```

Use `-m:1` so WinUI apps that share `bin\` do not race on `.xbf` outputs. CI runs the same build/test matrix (x64 Debug and Release) via `.github/workflows/ci.yml`.
