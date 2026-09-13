# DriveStat

A WinUI 3 disk usage statistics viewer inspired by [WinDirStat](https://windirstat.net/).

## Features

- **Directory tree** — Browse folders sorted by size (largest first)
- **Treemap** — Visual representation of disk usage with colored rectangles proportional to file size
- **Extension list** — Statistics by file type with size and file count

## Usage

1. Select a drive from the dropdown or click **Browse...** to pick a folder
2. Click **Scan** to analyze disk usage
3. Use the directory tree to drill down; the treemap updates to show the selected folder's contents
4. Right-click a folder in the tree and choose **Open in Explorer** to open it in File Explorer

## Building

```powershell
cd Source
dotnet build DriveStat\DriveStat.csproj -c Debug -p:Platform=x64
dotnet run --project DriveStat -p:Platform=x64
```

Executable output (repo root):

`bin\x64\Debug\net10.0-windows10.0.19041.0\DriveStat.exe`

## Requirements

- Windows 10 19041 or later / Windows 11
- .NET 10 SDK and Windows App SDK 2.x (see project file)
- Platform: x64, x86, or ARM64 (not AnyCPU)

## Related

- Suite overview: [Source/README.md](../README.md)
- Root README: [Disk Utilities](../../README.md)
