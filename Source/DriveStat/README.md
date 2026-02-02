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

```bash
dotnet build DriveStat\DriveStat.csproj -c Debug -p:Platform=x64
```

## Requirements

- Windows 10 19041 or later
- .NET 10.0 (or the target framework specified in the project)
