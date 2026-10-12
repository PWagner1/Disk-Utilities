# DiskSeer

WinUI 3 browser for **Apple** (HFS+, APFS) and **Linux** (ext2/3/4, Btrfs, XFS) volumes on Windows.

## Features

- Enumerate physical disks and foreign partitions (admin required for raw access)
- **Read-only mode on by default** — turn off only when you intentionally want experimental DiscUtils writes
- Browse, copy out to Windows, optional copy-in / rename / delete when writable
- **APFS** via optional libfsapfs (in-app banner + docs when the DLL is missing)
- Current-folder filter and **recursive search** (Enter / search icon with Recursive checked)
- Drag files to Explorer (stages a temp copy first)

## APFS

See [Docs/LIBFSAPFS_BUILD.md](Docs/LIBFSAPFS_BUILD.md). Place `libfsapfs-1.dll` and its dependencies next to `DiskSeer.exe`.

## Safety

- Prefer read-only on unfamiliar or important disks
- APFS is always read-only in DiskSeer
- DiscUtils write support for ext/HFS+/Btrfs/XFS is experimental — confirm before opening writable

## Build

```powershell
cd Source
dotnet build DiskSeer\DiskSeer.csproj -c Debug -p:Platform=x64
dotnet run --project DiskSeer -p:Platform=x64
```

Output: `bin\x64\Debug\net10.0-windows10.0.19041.0\DiskSeer.exe` (repo root).
