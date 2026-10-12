# Building libfsapfs for APFS Support

DiskSeer can **detect** APFS volumes without extra DLLs. **Browsing** APFS requires the native [libfsapfs](https://github.com/libyal/libfsapfs) library next to `DiskSeer.exe`.

## Quick checklist

1. Build or obtain `libfsapfs-1.dll` and its dependency DLLs (libcdata, libcerror, libcpath, …).
2. Copy them into the DiskSeer output folder, for example:

   `bin\x64\Debug\net10.0-windows10.0.19041.0\`

   (same folder as `DiskSeer.exe`).
3. Restart DiskSeer. The partition list should drop the `[needs libfsapfs]` suffix, and **Open** should work.

DiskSeer’s UI also shows this path when you select an APFS partition without the library.

## Prerequisites

- Visual Studio 2022 with C++ build tools
- CMake or the libfsapfs build environment
- libfsapfs dependencies (use the project’s `synclibs` / build wiki)

## Build steps

1. Download a release from https://github.com/libyal/libfsapfs/releases (e.g. experimental tarball).
2. Follow https://github.com/libyal/libfsapfs/wiki/Building (Visual Studio, Cygwin, or MinGW).
3. Place **all** produced DLLs beside `DiskSeer.exe`. The P/Invoke name is `libfsapfs-1`.

There is no in-app download: redistributing prebuilt libfsapfs binaries is left to packagers who accept the LGPL/license terms of libyal.

## Limitations

| Topic | Behavior |
|-------|----------|
| Access | **Read-only** (browse + copy out). Writes are not supported. |
| Staging | libfsapfs opens a file path; DiskSeer copies the partition to a temp `.raw` file (up to **128 GB**), with progress in the status bar. |
| Format | APFS v2; T2 encryption, LZFSE, and snapshots are not supported. |
| Detection | Magic `NXSB` detection works without the DLL; browsing does not. |
