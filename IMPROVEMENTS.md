# Disk Utilities — Codebase Audit & Improvement Suggestions

**Date:** 2026-09-13  
**Scope:** Full suite under `Source/` (DiskEraser, DiskHealth, DiskSeer, DriveStat, HandleFinder)  
**Stack:** C# / .NET 10 / WinUI 3 (Windows App SDK 2.4) — unpackaged desktop apps

This document summarizes the current state of the codebase and proposes concrete improvements and new features, grouped by priority and by application.

---

## 1. Executive summary

Disk Utilities is a coherent suite of five specialized WinUI tools (~14k lines of C#) with real depth in foreign file systems (DiskSeer), handle discovery (HandleFinder), and formatting/partitioning (DiskEraser). Engineering maturity is early: no automated tests, no CI, duplicated chrome/utilities across apps, documentation that drifts from the product, and several UX claims that oversell what Windows can actually do (e.g. Linux/Apple format options).

**Highest-leverage themes:**

1. Fix product identity and documentation so the suite matches reality.
2. Extract shared infrastructure and reduce god-files.
3. Add tests + CI for the non-UI core (signatures, path matching, diskpart scripts, scan totals).
4. Be honest and safer about destructive / unsupported operations.
5. Fill the natural feature gaps per app (NVMe SMART, MFT undelete, DriveStat actions, suite launcher).

---

## 2. Current state (audit snapshot)

| Area | Finding |
|------|---------|
| Projects | 5 independent WinUI apps in one solution; no shared class library |
| Approx. C# size | ~14k lines (DiskEraser largest ~5.5k) |
| Architecture | Models + Services + large code-behind windows; DiskSeer uses an FS adapter pattern |
| Tests | None |
| CI / Dependabot | None |
| Shared build config | No `Directory.Build.props` / `.editorconfig` / `global.json` |
| Packaging | Unpackaged; some `Package.appxmanifest` files exist without Assets / complete MSIX story |
| Docs | Root README still titled “Drive-Wiper” and covers only DiskEraser; `Source/README.md` omits DriveStat; HandleFinder docs are excellent |
| Privilege model | DiskSeer / HandleFinder require admin; DiskEraser / DiskHealth / DriveStat are `asInvoker` and fail at runtime without elevation for some ops |
| Error handling | Many empty `catch { }` blocks that return empty results |

### Strengths to keep

- Clear per-app Models/Services split
- Thoughtful async, cancellation, and parallelism on hot paths
- HandleFinder dual GUI + CLI (`--json`, `--kill`, `--wait`)
- DiskSeer adapter abstraction (`IForeignFileSystemAdapter`) and optional APFS native path
- Safety rail: system drive `C:\` excluded from DiskEraser format list
- Modern target (.NET 10 + Windows App SDK 2.x)

---

## 3. Cross-cutting improvements

### 3.1 Documentation & product identity (quick wins)

| Suggestion | Why |
|------------|-----|
| Rename/retitle root README from “Drive-Wiper” to **Disk Utilities** | Matches solution name and suite scope |
| Document **all five** apps in root and `Source/README.md` (add DriveStat) | Users currently discover DriveStat only by browsing the solution |
| Align TFM / SDK version notes (docs still mention .NET 8 in places; projects target **net10.0**) | Avoid build confusion |
| Add short READMEs for DiskEraser and DiskHealth (parity with HandleFinder / DriveStat) | Onboarding and expectation-setting |
| Add `CHANGELOG.md` and conventional commit / clearer history | Release notes and audits |
| Add a short security / safety section for wipe & recovery tools | Sets expectations vs forensic / enterprise tools |

### 3.2 Shared infrastructure

| Suggestion | Why |
|------------|-----|
| Create `DiskUtilities.Shared` (or similar) class library | Deduplicate Mica/title-bar helpers, byte formatting, elevation checks, theme/settings, WMI drive enumeration patterns |
| Introduce `Directory.Build.props` for shared TFM, package versions, analyzers, and `BaseOutputPath` | Prevent version drift across the five `.csproj` files |
| Add `.editorconfig` + nullable/analyzer rules consistently | Code style and catch empty catches |
| Optional thin MVVM (or CommunityToolkit.Mvvm) for new windows | Shrink 700–900 LOC code-behind files; improve testability |
| Central logging (e.g. debug file + optional user log folder) instead of silent catches | Diagnosability for admin/disk failures |

### 3.3 Engineering hygiene

| Suggestion | Why |
|------------|-----|
| Unit test projects for services (xUnit/NUnit) | Highest ROI: signature detection, HandleFinder path matching, diskpart script builders, DriveStat tree totals, recovery chance heuristics |
| GitHub Actions: build all platforms on PR; optionally run tests | Catch TFM/WASDK regressions early |
| Dependabot / Renovate for NuGet (WindowsAppSDK, DiscUtils, System.Management) | Security and API freshness |
| Replace empty `catch` with logged failures and user-visible status | Empty lists currently look like “no drives / no files” |
| Consistent elevation UX: detect admin, prompt to relaunch elevated when needed | Better than cryptic WMI/CreateFile failures |
| Complete or remove incomplete MSIX packaging path | Manifests without Assets confuse contributors |
| Threat / safety review of close-handle and kill-process flows | HandleFinder already documents limits; keep that bar across destructive ops |

### 3.4 Architecture / maintainability

Large files that would benefit from splitting:

- `DiskSeer/Services/ForeignDriveService.cs` (~900+ LOC)
- `DiskEraser/PartitionManagementWindow.xaml.cs` (~900+ LOC)
- Main windows across apps (700–800+ LOC each)

Suggested direction: keep UI thin; move orchestration into services or ViewModels; keep P/Invoke and diskpart behind small focused types.

---

## 4. Safety & honesty (product trust)

These are high-priority UX/docs fixes more than “new features”:

1. **DiskEraser format list** — Linux/Apple FS options typically fail on stock Windows via WMI. Prefer: disable/grey them with an explanation, or gate them behind detected third-party formatters — do not present them as first-class equals to NTFS/exFAT.
2. **“Secure deletion” wording** — Implementation is **free-space overwrite before format**, not full-disk Secure Erase / NVMe Sanitize / crypto-erase. Clarify in UI and docs; consider renaming presets so “DoD / Military” does not imply standards compliance the app cannot certify.
3. **DiskHealth recovery expectations** — Signature carving is not MFT/NTFS undelete. Surface that clearly; “recovery chance” should be labeled as heuristic.
4. **Destructive confirmations** — Standardize double-confirm for format, partition delete, wipe, kill process, and close handle (HandleFinder already does some of this well).
5. **Protected volumes** — Extend `C:\` protection thinking to EFI/recovery/BitLocker system partitions where relevant.

---

## 5. Feature suggestions by application

### 5.1 DiskEraser

**Improvements**

- Clear unsupported FS options or integrate a documented external formatter path
- Show wipe vs format progress separately; estimate remaining time
- Persist last-used format options (label, FS, allocation unit, quick format)
- Better diskpart error surfacing (capture stdout/stderr into the UI)
- Undo/preview for shrink/extend (show before/after diagram before applying)
- Optional full-volume overwrite (not only free space) with strong warnings
- Hardware erase where available: ATA Secure Erase / NVMe Format/Sanitize (advanced, admin, with capability detection)

**New features**

- GPT/MBR conversion helpers with clear risk copy
- Partition alignment hints for advanced users
- Export/import partition layout diagrams
- Batch format of multiple non-system volumes (with per-volume confirm)
- CLI mode for scripted wipe/format (mirroring HandleFinder’s automation story)

### 5.2 DiskHealth

**Improvements**

- Expand signature set (e.g. 7z, RAR, WebP, HEIC, SQLite, EXE/DLL, ISO, MKV, FLAC) with footer/size heuristics where possible
- Reduce false positives on ZIP vs Office Open XML (already partially special-cased)
- Better NVMe health: expose NVMe SMART/log pages, not only WMI failure-predict for ATA
- Scan filters (by type, size, date estimate) and pause/resume
- Preview pane for images/PDFs before recover

**New features**

- NTFS MFT / Recycle Bin–aware undelete path alongside carving
- Deep scan vs quick scan modes
- Recovered-file quarantine report (CSV/JSON)
- SMART history / trend (store last N readings)
- Temperature / wear / spare capacity cards for SSDs
- Optional read-only image of volume before scan (forensic-lite)

### 5.3 DiskSeer

**Improvements**

- Ship or clearly package **libfsapfs** (or document a one-click download) so APFS is not a dead end after detection
- Update `Source/README.md` APFS section to match actual optional native support
- Raise or stream APFS copy limits (avoid full ≤32 GB temp copy where possible)
- Surface DiscUtils write limitations per FS in the UI before mutating
- Search within current directory / recursive find
- Symlink / hardlink / permission display fidelity improvements

**New features**

- Mount-as-folder or virtual drive letter (advanced)
- Checksum / compare file on foreign volume vs Windows copy
- Read-only mode toggle (default on for unfamiliar disks)
- Support additional FS if DiscUtils/community libs allow (e.g. more complete Btrfs features)
- Drag-and-drop copy to Explorer

### 5.4 DriveStat

**Improvements**

- Faster rescans (incremental / watch folder / parallel directory walk with better IO scheduling)
- Hard links, junctions, and reparse-point handling (avoid double-counting)
- Cancel + partial results already useful — add “scan from here” on a subtree
- Persist color map for extensions (WinDirStat-like)

**New features**

- Delete / move / recycle from the UI (with safety confirm)
- Compare two scans / “what grew since last time”
- Export report (HTML/CSV)
- Duplicate-file finder (hash or size+name heuristics)
- Free-space forecast / largest cleanup candidates list
- CLI: `DriveStat --path D:\ --json` for scripting

### 5.5 HandleFinder

**Improvements** (already the most polished)

- Keep docs in sync with actual output paths / TFM (`net10` vs older `net8` paths in README)
- Show access mask decoded more friendly in the UI
- Filter by process name / PID / handle type
- “Restart as admin” banner if somehow launched elevated incorrectly

**New features**

- Watch mode: refresh until lock clears (GUI complement to `--wait`)
- Integrate with Explorer context menu (“What has this locked?”)
- Optional close-handle batch with per-handle confirm
- Module / memory-mapped section insights beyond file handles (clearly labeled as limited)
- PowerToys-style “as a tool” packaging if you ever ship a suite installer

---

## 6. Suite-level product features

| Feature | Description |
|---------|-------------|
| **Suite launcher** | Small shell that lists the five tools with elevation hints and short descriptions |
| **Single installer** | Inno Setup / MSIX / winget manifest for the whole suite |
| **Shared settings** | Theme (light/dark/system), accent, “always prompt for destructive ops” |
| **Cross-links** | e.g. from DiskEraser “this volume is locked” → open HandleFinder on that path; from DriveStat → DiskHealth for a selected drive |
| **Localization** | Resource files (en first, then others) — all strings currently inline |
| **Accessibility** | Keyboard navigation pass, AutomationProperties, high-contrast check |
| **Optional telemetry** | Opt-in crash/usage only; default off for a utilities suite |
| **winget / Scoop / Chocolatey** | Distribution beyond GitHub Releases |

---

## 7. Suggested roadmap (prioritized)

### P0 — Trust & clarity (1–2 weeks)

- [x] Fix root + Source READMEs (suite name, all five apps, .NET 10) — [#1](https://github.com/PWagner1/Disk-Utilities/issues/1)
- [x] Honest DiskEraser FS + secure-wipe copy in UI — [#2](https://github.com/PWagner1/Disk-Utilities/issues/2)
- [x] Honest DiskHealth carving vs undelete copy — [#3](https://github.com/PWagner1/Disk-Utilities/issues/3)
- [x] Elevation detection / relaunch prompts where ops require admin — [#4](https://github.com/PWagner1/Disk-Utilities/issues/4)

### P1 — Foundation (2–4 weeks)

- [ ] `Directory.Build.props` + shared project for window chrome / helpers
- [ ] Logging instead of silent catches on service boundaries
- [ ] Unit tests for RecoveryService signatures, HandleService path rules, DriveService script generation, ScanService aggregates
- [ ] GitHub Actions build (x64 Debug/Release)

### P2 — High-value features (ongoing)

- [ ] DriveStat: delete/recycle + export + hardlink-aware scan
- [ ] DiskHealth: more signatures + NVMe SMART depth + filters/preview
- [ ] DiskSeer: APFS packaging UX + search + read-only default
- [ ] DiskEraser: wipe/format progress ETA + unsupported FS gating
- [ ] Suite launcher + GitHub Releases automation

### P3 — Differentiation

- [ ] Hardware secure erase / NVMe sanitize (capability-gated)
- [ ] NTFS MFT-aware recovery
- [ ] Explorer context-menu integration (HandleFinder / DriveStat)
- [ ] Full MSIX or winget distribution
- [ ] Localization + accessibility pass

---

## 8. Testing recommendations (concrete)

| Target | Example cases |
|--------|----------------|
| `RecoveryService` | Known buffers contain JPEG/PNG/PDF; ZIP vs DOCX discrimination; empty/short buffers |
| `HandleService` / CLI | Path normalization, folder prefix matching, JSON output shape |
| `DriveService` | diskpart script text for create/delete/shrink/extend; C: exclusion |
| `ScanService` | Tree size equals sum of children; extension tallies; permission-denied dirs |
| `DiscUtilsAdapter` | Temp fixtures for small ext/HFS images if feasible in CI |
| UI (optional later) | Smoke: launch each app; critical dialogs present |

---

## 9. Out of scope / non-goals (unless product direction changes)

- Becoming a full forensic suite (FTK/Autopsy class)
- Kernel drivers for handle unlocking (HandleFinder correctly avoids this)
- Guaranteeing secure erase against remapped SSD cells without hardware commands
- Supporting every Linux/Apple FS edge case better than dedicated OS tools

---

## 10. Conclusion

The suite already delivers useful, differentiated tools on a modern WinUI stack. The best next steps are not a rewrite: **clarify the product story**, **share infrastructure**, **test the dangerous/core logic**, and **ship a few high-visibility features** (DriveStat actions, richer DiskHealth signatures/SMART, DiskSeer APFS UX, DiskEraser honesty around wipe/format). Those changes will make the project easier to maintain and more trustworthy for users who depend on it for disk work.
