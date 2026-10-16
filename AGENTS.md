# AGENTS.md

Guidance for AI agents and contributors working in this repository.

## What this repo is

**Disk Utilities** is a suite of five unpackaged **WinUI 3** desktop apps for Windows 10 (19041+) / Windows 11:

| App | Role |
|-----|------|
| **SuiteLauncher** | Suite entry point (launch tools, theme seed) |
| **DiskEraser** | Format volumes and manage partitions |
| **DiskHealth** | Signature-based file recovery + S.M.A.R.T. health |
| **DiskSeer** | Browse/edit Apple and Linux file systems from Windows |
| **DriveStat** | Disk usage (tree, treemap, extensions) |
| **HandleFinder** | Find which processes lock files/folders/drives (GUI + CLI) |

Shared code lives in `Source/DiskUtilities.Shared/`. Solution: `Source/Disk Utilities.sln`.
Build defaults (TFM, package versions, app output under repo `bin\`) live in `Source/Directory.Build.props`.

Docs: root [README.md](README.md), [Source/README.md](Source/README.md), per-app READMEs, [CHANGELOG.md](CHANGELOG.md), [IMPROVEMENTS.md](IMPROVEMENTS.md).

## Stack

- **.NET 10** (`net10.0-windows10.0.19041.0`), nullable enabled, implicit usings
- **Windows App SDK 2.x** (self-contained); `WindowsPackageType=None` (unpackaged)
- Platform must be **x64**, x86, or ARM64 — not AnyCPU
- Typical deps: `System.Management` (WMI), DiscUtils (DiskSeer), optional native libfsapfs for APFS

## Build and run

```powershell
cd Source
dotnet build "Disk Utilities.sln" -c Debug -p:Platform=x64
dotnet test DiskUtilities.Tests -c Debug -p:Platform=x64
dotnet run --project DiskEraser -p:Platform=x64
```

Binaries: `bin\<platform>\<config>\net10.0-windows10.0.19041.0\<App>\<App>.exe` at the repo root. Each WinUI app has its own folder so `App.xbf` and `resources.pri` are not overwritten.

Prefer Visual Studio if CLI XamlCompiler fails. Do not commit `obj/`, `.vs/`, or build outputs under `bin/`.

## Architecture conventions

- Per app: **Models/**, **Services/**, WinUI windows (XAML + code-behind)
- Put reusable suite helpers in **DiskUtilities.Shared**, not copy-pasted across apps
- Keep long I/O and WMI work off the UI thread; marshal results back to the dispatcher
- Prefer cancellation tokens on scans, copies, and erase/overwrite loops
- Match existing naming, namespaces, and project structure; avoid drive-by refactors

## Safety (non-negotiable)

These tools can destroy data. Agents must preserve and strengthen safety rails, never weaken them for convenience.

- **DiskEraser**: never format protected volumes (C:, boot/system, EFI/recovery). Free-space overwrite = unused space fill before format only. Hardware erase (NVMe Sanitize) is capability-gated, admin-only, refuses disks with protected volumes, and must not claim certification; ATA Secure Erase stays detect-only (not automated). Do not re-enable Linux/Apple format as working on stock Windows.
- **DiskHealth**: keep **Carve** vs **Recycle Bin** vs **MFT deleted** distinct; carving is not undelete. Keep “chance” labeled as a heuristic; do not claim full forensic undelete.
- **DiskSeer**: requires admin and explicit partition selection; APFS browsing needs optional libfsapfs (read-only). Prefer read-only defaults on unfamiliar disks.
- **HandleFinder**: closing handles / ending processes is destructive; keep confirmations. Prefer ending the locking app when documenting UX.
- **DriveStat**: treat as analysis-first; do not add silent destructive actions.
- Do not soften warning copy or remove confirmations on destructive paths.
- Always update safety/docs when changing wipe, format, partition, recovery, or handle-kill behavior.

## Documentation expectations

- Keep root README, `Source/README.md`, and the affected app README aligned with behavior
- Record user-facing changes in `CHANGELOG.md` under `[Unreleased]`
- Do not claim DoD / military wipe certification, full undelete, or hardware sanitize unless the code actually does that
- Avoid inventing features in docs that Windows/WMI cannot support

## What to avoid

- Expanding scope beyond the requested app or change
- Empty `catch { }` that hides disk/admin failures — use `AppLog` (or surface errors) when touching those paths; logs go to `%LocalAppData%\DiskUtilities\logs\`
- Committing secrets, credentials, or local VS/cache junk
- Creating commits or PRs unless the user explicitly asks

## Suggested workflow

1. Identify which app(s) and whether shared code is involved
2. Read the app’s README and the relevant Service before changing behavior
3. Implement the smallest correct change; keep safety and docs in sync
4. Build the affected project with `-p:Platform=x64` when practical
