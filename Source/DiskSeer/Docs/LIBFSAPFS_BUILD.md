# Building libfsapfs for APFS Support

DiskSeer can read Apple File System (APFS) volumes when the libfsapfs library is available.

## Prerequisites

- Visual Studio 2022 with C++ build tools
- CMake or the libfsapfs build environment
- libfsapfs dependencies (libcdata, libcerror, etc. - use synclibs)

## Build Steps

1. **Download libfsapfs** from https://github.com/libyal/libfsapfs/releases (e.g. libfsapfs-experimental-20240429.tar.gz)

2. **Extract and build** - See https://github.com/libyal/libfsapfs/wiki/Building
   - On Windows, use the Visual Studio build or Cygwin/MinGW
   - Run synclibs to get dependencies

3. **Place DLLs** in the DiskSeer output directory (next to DiskSeer.exe):
   - libfsapfs-1.dll
   - All dependency DLLs (libcdata, libcerror, libcpath, etc.)

## Limitations

- **Read-only**: APFS support is read-only (browse, copy out)
- **Partition size**: Partitions larger than 32 GB are copied to a temp file; smaller partitions work directly
- **APFS version 2** only; T2 encryption, LZFSE, snapshots not supported
