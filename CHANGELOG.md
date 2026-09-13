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

- Suite documentation identity: root README covers all five apps (DiskEraser, DiskHealth, DiskSeer, DriveStat, HandleFinder)
- Per-app READMEs for DiskEraser and DiskHealth
- Safety / expectations sections for wipe, recovery, and related tools
- This changelog
- **`DiskUtilities.Shared`:** elevation detection, UAC relaunch, and WinUI banner/prompt helpers used by all apps
- Unelevated warning banners and “Restart as administrator” flows in DiskEraser, DiskHealth, DiskSeer, DriveStat, and HandleFinder
