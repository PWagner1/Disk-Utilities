# Disk Utilities – Source

This folder contains three WinUI 3 desktop applications for disk management:

- **DiskEraser** – Format drives with secure deletion options
- **DiskHealth** – Recover deleted files from drives
- **DiskSeer** – Read and modify drives formatted with Apple or Linux file systems

## Structure

- **DiskEraser/** – WinUI 3 project for drive formatting
  - **MainWindow.xaml** – Drive selection, format options, partition layout diagram
  - **PartitionManagementWindow.xaml** – Create, delete, extend, and shrink partitions
  - **Models/** – DiskInfo, DriveItem, PartitionInfo models
  - **Services/DriveService.cs** – WMI-based drive enumeration, formatting, and diskpart operations

- **DiskHealth/** – WinUI 3 project for file recovery
  - **MainWindow.xaml** – Drive selection, scan, recoverable file list with recovery chances
  - **Models/RecoverableFileInfo.cs**, **ScannableDrive** – Models for scan results
  - **Services/RecoveryService.cs** – Drive enumeration, file-signature scanning, and recovery

- **DiskSeer/** – WinUI 3 project for accessing foreign file systems
  - **MainWindow.xaml** – Drive/partition selection, file browser with breadcrumb navigation
  - **Models/** – ForeignDriveInfo, ForeignPartitionInfo, FileSystemEntry, BreadcrumbItem
  - **Services/ForeignDriveService.cs** – Detection and access to Apple/Linux file systems via DiscUtils

## Requirements

- Windows 10 19041+ or Windows 11
- .NET 8 SDK
- Visual Studio 2022 with **Windows application development** (WinUI) workload, or build from command line with `dotnet build`
- **Administrator rights** – Required for DiskSeer to access raw disk partitions

## Build and run

From the repo root or from `Source`:

```bash
cd Source
dotnet build "Disk Utilities.sln" -c Debug -p:Platform=x64
dotnet run --project DiskEraser -p:Platform=x64
# or
dotnet run --project DiskHealth -p:Platform=x64
# or
dotnet run --project DiskSeer -p:Platform=x64
```

Or open `Source/Disk Utilities.sln` in Visual Studio and press F5.

## DiskEraser – Secure deletion

When **Secure deletion** is enabled, the app overwrites all free space on the selected drive with cryptographically random data for the chosen number of passes, then formats. Presets:

- **1 pass** – Quick overwrite
- **3 passes (DoD 5220.22-M)** – U.S. DoD standard
- **7 passes (Military grade)** – Multiple overwrites for high-sensitivity data

## DiskSeer – Foreign File Systems

DiskSeer can read and write drives formatted with non-Windows file systems:

**Supported file systems (read/write):**
- **Linux**: ext2, ext3, ext4, Btrfs, XFS
- **Apple**: HFS+ (read/write)

**Detected but not fully supported:**
- **APFS** – Detected but not supported for browsing (Apple's newest file system uses encryption and compression that DiscUtils doesn't fully support)

### Features
- Browse files and folders on foreign file systems
- Copy files from foreign drives to Windows
- Copy files from Windows to foreign drives
- Create, rename, and delete files and folders
- View file properties (permissions, timestamps, sizes)

### How it works
DiskSeer uses the [DiscUtils](https://github.com/DiscUtils/DiscUtils) library to parse file system structures directly from raw disk sectors. This requires administrator privileges to access `\\.\PhysicalDriveN` devices.

## File systems (DiskEraser)

The format UI offers:

- **Windows**: NTFS, FAT32, FAT, exFAT, ReFS  
- **Linux**: ext2, ext3, ext4, Btrfs, XFS  
- **Apple**: HFS, HFS+, APFS  

Formatting is done via WMI `Win32_Volume.Format`. Windows natively supports NTFS, FAT, and FAT32; exFAT and ReFS may work on newer Windows. Linux and Apple file systems typically return "Unsupported file system" unless you use third-party drivers.

## Safety

- The **C:\** drive is never listed and cannot be selected for formatting in DiskEraser
- DiskSeer requires explicit partition selection before any operations

## Packaging

Projects are configured as **unpackaged** (`WindowsPackageType=None`) for simple build and run. To produce an MSIX package, set `WindowsPackageType` to `MSIX`, add `Package.appxmanifest`, and include the required Assets.
