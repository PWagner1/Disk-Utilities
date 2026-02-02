# HandleFinder

**HandleFinder** is a WinUI 3 desktop application that identifies which processes have open handles (file locks) on a given file, folder, or entire drive. It helps resolve "Access Denied," "File in Use," and similar errors by showing exactly which programs are locking resources.

---

## Table of Contents

1. [Overview](#overview)
2. [Features](#features)
3. [Requirements](#requirements)
4. [Installation & Build](#installation--build)
5. [Usage Guide](#usage-guide)
6. [Technical Architecture](#technical-architecture)
7. [API Reference](#api-reference)
8. [Troubleshooting](#troubleshooting)
9. [Security](#security)
10. [Limitations](#limitations)

---

## Overview

When Windows reports that a file or folder cannot be deleted, renamed, or moved because it is "in use," HandleFinder can determine which process is holding the handle. Unlike tools like [Unlocker](http://www.emptyloop.com/unlocker/) that rely on kernel drivers to close handles, HandleFinder focuses on **discovery**—it shows which processes lock a path and allows you to terminate those processes if desired.

### Use Cases

- **File deletion**: "Cannot delete file: Access is denied"
- **Folder operations**: "The folder is in use by another program"
- **Drive unmount**: "The drive is in use"
- **Development**: Identifying locked files during builds or deployments
- **System maintenance**: Finding what prevents safe removal of drives or folders

---

## Features

| Feature | Description |
|---------|-------------|
| **Drive support** | Search entire volumes (e.g., `C:\`) |
| **Folder support** | Find handles to a folder or any file within it |
| **File support** | Find handles to a specific file |
| **Drive selector** | Quick-select from a dropdown of available drives |
| **Browse** | Browse for a file or folder via the picker |
| **Close handle** | Close the selected handle without terminating the process |
| **End process** | Terminate the selected process (with confirmation) |
| **Process details** | Process name, PID, handle type, and full path |
| **Cross-version** | Automatic fallback for different Windows ObjectTypeIndex values |

---

## Requirements

| Requirement | Details |
|-------------|---------|
| **OS** | Windows 10 (19041+) or Windows 11 |
| **Architecture** | x64, x86, or ARM64 |
| **Privileges** | Administrator (required for handle enumeration) |
| **Build** | .NET 8 SDK or later; Visual Studio 2022 with Windows App SDK workload |

---

## Installation & Build

### From Visual Studio

1. Open `Source/Disk Utilities.sln`.
2. Set platform to **x64** (or x86/ARM64).
3. Right-click **HandleFinder** → **Set as Startup Project**.
4. Build and run (F5).

### From Command Line

```powershell
cd Source
dotnet build HandleFinder\HandleFinder.csproj -c Debug -p:Platform=x64
```

Run the executable:

```powershell
.\HandleFinder\bin\x64\Debug\net8.0-windows10.0.19041.0\HandleFinder.exe
```

> **Important**: Always run as Administrator. Right-click the executable and choose "Run as administrator," or use an elevated command prompt.

---

## Usage Guide

### Step 1: Specify the Path

Choose one of these methods:

| Method | Description |
|--------|-------------|
| **Drive combo** | Select a drive (e.g., C:) from the dropdown. The path box is filled with the drive root. |
| **Path box** | Type or paste a path: `C:\`, `D:\`, `C:\path\to\folder`, or `C:\path\to\file.txt`. |
| **Browse folder** | Select a folder from the picker. |
| **Browse file** | Select a specific file from the picker. |

### Step 2: Find Handles

Click **Find handles**. The app scans the system for file handles that match your path:

- **Drive** (`C:\`): Matches any handle to a file or folder on that drive.
- **Folder** (`C:\MyFolder`): Matches any handle to a file or folder inside that folder.
- **File** (`C:\file.txt`): Matches only handles to that exact file.

### Step 3: Review Results

The results list shows:

| Column | Description |
|--------|-------------|
| **Process** | Name of the process (e.g., `explorer.exe`) |
| **PID** | Process ID |
| **Path** | Full path of the handle (hover for full text if truncated) |

### Step 4: Close Handle or End Process (Optional)

**Close handle** (preferred when possible):
1. Select a row in the list.
2. Click **Close handle**.
3. Confirm in the dialog. The handle is closed in the target process; the process continues running.

**End process** (when close handle fails or is not appropriate):
1. Select a row in the list.
2. Click **End process**.
3. Confirm in the dialog.

> **Warning**: Closing a handle may cause the target process to crash if it is actively using the file. Ending a process may cause data loss or instability.

---

## Technical Architecture

### High-Level Flow

```
User input (path) → Path validation → NtQuerySystemInformation
                                        ↓
                              Enumerate all system handles
                                        ↓
                              Filter by ObjectTypeIndex (File = 28)
                                        ↓
                              For each handle: OpenProcess → NtDuplicateObject
                                        ↓
                              GetFinalPathNameByHandleW → resolve path
                                        ↓
                              PathMatches(target, handlePath) → add to results
```

### File Structure

```
HandleFinder/
├── App.xaml / App.xaml.cs      # Application entry, Mica backdrop
├── MainWindow.xaml / .cs       # Main UI, path input, results list
├── GlobalUsings.cs             # Global using directives
├── app.manifest                # requireAdministrator
├── Models/
│   ├── HandleInfo.cs          # Process ID, name, handle value, path, access
│   └── DriveItem.cs           # Drive display for combo
├── Services/
│   └── HandleService.cs       # Handle enumeration logic
└── README.md                  # This file
```

### Windows APIs Used

| API | Purpose |
|-----|---------|
| `NtQuerySystemInformation` (class 64) | Enumerate all system handles |
| `NtDuplicateObject` | Duplicate handle, or close handle in another process (`DUPLICATE_CLOSE_SOURCE`) |
| `OpenProcess` | Open target process with `PROCESS_DUP_HANDLE` |
| `GetFinalPathNameByHandleW` | Resolve handle to its file path |
| `CloseHandle` | Release duplicated handles |

### Path Matching Logic

- **Exact match**: Target path equals handle path.
- **Directory containment**: Target is a folder; handle path starts with `target\` or `target/`.

Paths are normalized (full path, trimmed trailing slashes, case-insensitive) before comparison.

---

## API Reference

### HandleService

#### `FindHandlesForPathAsync(string targetPath, bool comprehensiveSearch = false, CancellationToken cancellationToken = default)`

Finds all processes that have file handles open to the given path. Automatically retries with comprehensive search if no results are found (handles different Windows ObjectTypeIndex values).

**Parameters:**
- `targetPath`: Full path to a file, folder, or drive. Examples: `C:\`, `D:\Data`, `C:\file.txt`.
- `comprehensiveSearch`: If true, tries all handle types (slower). Used automatically as fallback when no results.
- `cancellationToken`: Optional cancellation token.

**Returns:** `Task<IReadOnlyList<HandleInfo>>`

**Example:**
```csharp
var service = new HandleService();
var handles = await service.FindHandlesForPathAsync("C:\\Windows");
```

#### `TryCloseHandle(int processId, nuint handleValue)`

Attempts to close a handle in another process without terminating it. Uses `NtDuplicateObject` with `DUPLICATE_CLOSE_SOURCE`.

**Returns:** `bool` — `true` if handle was closed; `false` otherwise (e.g., protected process, older Windows).

#### `TryTerminateProcess(int processId)`

Attempts to terminate a process by ID.

**Returns:** `bool` — `true` if process was terminated; `false` otherwise.

---

### HandleInfo

| Property | Type | Description |
|---------|------|-------------|
| `ProcessId` | `int` | Process ID |
| `ProcessName` | `string` | Process name (e.g., `explorer.exe`) |
| `HandleValue` | `nuint` | Handle value in the process |
| `ObjectTypeIndex` | `ushort` | Object type (28 = File on Windows 10/11) |
| `Path` | `string` | Resolved file or folder path |
| `GrantedAccess` | `uint` | Access rights on the handle |

---

### DriveItem

| Property | Type | Description |
|---------|------|-------------|
| `Path` | `string` | Drive root (e.g., `C:\`) |
| `DisplayText` | `string` | Display text (e.g., `C:`) |

---

## Troubleshooting

### "Path not found" for valid paths

- Ensure the path exists and is accessible.
- For drives, use `C:\` (with backslash).
- For network paths, use `\\server\share`.

### No results when handles exist

- **Run as Administrator**: HandleFinder requires elevated privileges.
- **Antivirus**: Some security software may block handle enumeration.
- **Protected processes**: System processes may not be accessible.
- **Object type**: Only file handles (ObjectTypeIndex 28) are enumerated; other types are ignored.

### "Could not close handle"

- Try **End process** instead.
- The process may be protected (e.g., Protected Process Light).
- `DUPLICATE_CLOSE_SOURCE` may not work on older Windows (e.g., Windows 7).

### "Failed to end process"

- The process may be protected (e.g., kernel/system).
- The process may have already exited.
- Antivirus or security software may block termination.

### Slow search

- Searching a drive can take 30–60 seconds or more due to many handles.
- The process is asynchronous; the UI remains responsive.

### Build errors

- **Platform**: Build with `-p:Platform=x64` (or x86/ARM64). AnyCPU is not supported.
- **Windows App SDK**: Ensure the Microsoft.WindowsAppSDK package is restored.

---

## Security

- **Administrator required**: The app uses `requireAdministrator` in its manifest to obtain `SeDebugPrivilege`, which is needed to open other processes and duplicate handles.
- **Handle closing**: Uses `NtDuplicateObject` with `DUPLICATE_CLOSE_SOURCE` to close handles without terminating processes. May not work on all Windows versions or for protected processes.
- **Process termination**: Ending a process is intentional and requires user confirmation.
- **No telemetry**: The application does not collect or transmit data.

---

## Limitations

1. **File handles only**: Only file and directory handles are enumerated; other object types (e.g., registry keys) are not supported. A fallback tries multiple ObjectTypeIndex values for compatibility across Windows versions.
2. **Process protection**: Some processes (e.g., Protected Process Light) may be inaccessible. Handle closing and process termination will fail for these; the app skips them during enumeration.
3. **Handle closing compatibility**: `DUPLICATE_CLOSE_SOURCE` may not work on older Windows versions (e.g., Windows 7); use "End process" as a fallback.

---

## Related Tools

- [Sysinternals Handle](https://learn.microsoft.com/en-us/sysinternals/downloads/handle) — Command-line tool; can close handles.
- [Unlocker](http://www.emptyloop.com/unlocker/) — Uses a kernel driver to close handles.
- [Process Explorer](https://learn.microsoft.com/en-us/sysinternals/downloads/process-explorer) — Shows handles per process.

---

## License

See the repository root [LICENSE](../../LICENSE).
