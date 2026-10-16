# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/) where version tags are used.

## [Unreleased]

### Changed

- Root README retitled from “Drive-Wiper” to **Disk Utilities**
- `Source/README.md` updated for DriveStat, .NET 10 / Windows App SDK 2.x, and optional APFS (libfsapfs)
- HandleFinder README build requirements and output paths aligned with `net10.0-windows10.0.19041.0` and repo-root `bin\` output
- Light DriveStat README alignment for build/output paths
- **DiskEraser:** Linux/Apple format options disabled in UI; free-space overwrite copy no longer claims DoD/military certification; protect boot/EFI/recovery volumes beyond `C:`
- **DiskHealth:** UI and labels clarify signature carving vs undelete; recovery chance shown as heuristic estimate

### Added

- **Localization / a11y seed:** `SuiteResources` (en.json), `AccessibilityUi`, SuiteLauncher AccessKeys + AutomationProperties; [`docs/ACCESSIBILITY.md`](docs/ACCESSIBILITY.md)
- **Distribution:** `packaging/` winget + Scoop + Chocolatey skeletons, optional Inno script, `New-ManifestsFromTag.ps1`; release workflow emits `SHA256SUMS.txt` (MSIX deferred)
- **Explorer menus:** SuiteLauncher installs per-user HKCU verbs — “What has this locked?” (HandleFinder `--gui`) and “Analyze disk usage” (DriveStat `--path`); `--install-shell` / `--uninstall-shell`
- **DiskHealth:** Recycle Bin undelete, scoped NTFS MFT deleted-record scan, optional read-only volume image; UI separates Carve vs Recycle Bin vs MFT sources
- **DiskEraser:** capability-gated hardware erase probe (ATA/NVMe); NVMe Sanitize when advertised (admin, protected disks refused, typed confirm); ATA Secure Erase detected but not automated
- **HandleFinder:** process/PID/type filters; decoded access mask; Watch until clear; multi-select close-handle with per-handle confirm; `--gui [path]`
- **SuiteLauncher:** WinUI entry point listing the five tools with elevation hints; suite theme + destructive-ack preference in `%LocalAppData%\DiskUtilities\suite-settings.json`
- **DiskUtilities.Shared:** `SuiteApps` / `SuiteSettings` for launch + shared seed; HandleFinder `--gui [path]` for cross-links; DriveStat tree menu “What has this locked?”
- GitHub Releases workflow (tag `v*` → x64 zip of suite binaries)
- **DiskEraser:** wipe progress with ETA; separate format-phase status; persist last-used format options; diskpart stdout/stderr in partition UI + AppLog; shrink/extend before/after size preview
- Suite documentation identity: root README covers all five apps (DiskEraser, DiskHealth, DiskSeer, DriveStat, HandleFinder)
- Per-app READMEs for DiskEraser and DiskHealth
- Safety / expectations sections for wipe, recovery, and related tools
- This changelog
- **`DiskUtilities.Shared`:** elevation detection, UAC relaunch, WinUI banner/prompt helpers, `WindowChrome`, and `ByteFormatter`
- Unelevated warning banners and “Restart as administrator” flows in DiskEraser, DiskHealth, DiskSeer, DriveStat, and HandleFinder
- `Source/Directory.Build.props` (+ targets) for shared TFM, package versions, and app `BaseOutputPath`
- Root `.editorconfig` for consistent C# / project formatting
- **`AppLog`** in `DiskUtilities.Shared`: Debug + daily file under `%LocalAppData%\DiskUtilities\logs\`; service-boundary failures logged and surfaced in UI status when drive lists are empty
- **`DiskUtilities.Tests`** (xUnit): RecoveryService signatures, DriveService protection/diskpart builders, HandleService path rules, ScanService totals, ByteFormatter/AppLog
- GitHub Actions CI (x64 Debug/Release build + test) and Dependabot for NuGet / Actions
- **`DestructiveConfirm`** in `DiskUtilities.Shared`: Cancel-default confirm + acknowledgement-checkbox confirm for irreversible ops (format, partition delete, DiskSeer delete, end process); close-handle stays single confirm
- **DriveStat:** hard-link dedupe + skip junction/symlink descent; CSV/HTML export; persist extension colors; tree context menu for scan-from-here, move, Recycle Bin, permanent delete
- **DiskHealth:** expanded signatures (7z/RAR/WebP/HEIC/SQLite/EXE/ISO/MKV/FLAC/MP4); stronger ZIP vs Office OOXML detection; NVMe health log (temp/spare/wear cards); quick/deep carve, pause/resume, type filter, preview pane, CSV/JSON export
- **DiskSeer:** read-only default + write gating; APFS libfsapfs banner/docs/DLL-folder helpers; APFS temp staging up to 128 GB with progress; recursive search; drag-to-Explorer; clearer per-FS capability copy

### Fixed

- **Suite launch:** each WinUI app builds into its own folder (`bin\<platform>\<config>\<tfm>\<App>\`) so `App.xbf`, `MainWindow.xbf`, and `resources.pri` are not overwritten by the next project. SuiteLauncher starts sibling folders.
- **DiskEraser:** `NormalizeLetter` now treats `C:\` / `C/` as the protected system volume (previously appended a stray `:`)

### Changed

- HandleFinder CLI `--kill` help/docs clarify no-prompt automation behavior; stderr warns before terminating

### Removed

- Incomplete `Package.appxmanifest` files (no Assets / incomplete MSIX story); suite remains unpackaged
