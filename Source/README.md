# Drive Wiper – Source

This folder contains the **Drive Wiper** application: a WinUI 3 desktop tool to format drives with support for common file systems. The system drive (`C:\`) cannot be formatted.

## Structure

- **DriveWiper/** – Main WinUI 3 (Windows App SDK) project
  - **App.xaml / App.xaml.cs** – Application entry and resources
  - **MainWindow.xaml / MainWindow.xaml.cs** – Main UI: drive list, format options, and actions
  - **Models/DriveItem.cs** – Model for a logical drive (letter, label, file system, size)
  - **Services/DriveService.cs** – WMI-based drive enumeration and formatting (Win32_Volume)

## Requirements

- Windows 10 19041+ or Windows 11
- .NET 8 SDK
- Visual Studio 2022 with **Windows application development** (WinUI) workload, or build from command line with `dotnet build`

## Build and run

From the repo root or from `Source`:

```bash
cd Source
dotnet build DriveWiper.sln -c Debug
dotnet run --project DriveWiper
```

Or open `Source/DriveWiper.sln` in Visual Studio and press F5.

## Secure deletion

When **Secure deletion** is enabled, the app overwrites all free space on the selected drive with cryptographically random data for the chosen number of passes, then formats. Presets:

- **1 pass** – Quick overwrite
- **3 passes (DoD 5220.22-M)** – U.S. DoD standard
- **7 passes (Military grade)** – Multiple overwrites for high-sensitivity data

Progress is shown during the wipe. Format options (file system, label, etc.) apply after the wipe.

## File systems

The UI offers:

- **Windows**: NTFS, FAT32, FAT, exFAT, ReFS  
- **Linux**: ext2, ext3, ext4, Btrfs, XFS  
- **Apple**: HFS, HFS+, APFS  

Formatting is done via WMI `Win32_Volume.Format`. Windows natively supports NTFS, FAT, and FAT32; exFAT and ReFS may work on newer Windows. ext, APFS, HFS, etc. are listed for completeness—formatting them on Windows typically returns “Unsupported file system” unless you use third-party drivers or tools (e.g. WSL for ext). If a file system is unsupported, the app shows the WMI result message.

## Safety

- The **C:\** drive is never listed and cannot be selected for formatting. The service excludes it when enumerating drives and rejects format requests for it.

## Packaging

The project is configured as **unpackaged** (`WindowsPackageType=None`) for simple build and run. To produce an MSIX package, set `WindowsPackageType` to `MSIX`, add `Package.appxmanifest`, and include the required Assets (e.g. StoreLogo, SplashScreen) in an `Assets` folder.
