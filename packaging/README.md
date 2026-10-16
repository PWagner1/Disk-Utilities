# Disk Utilities — Distribution

## Decision (current)

| Path | Status |
|------|--------|
| **Unpackaged WinUI apps** + GitHub Releases zip | **Primary** — already built by `.github/workflows/release.yml` |
| **winget** (portable zip installer) | Manifests under `winget/` — submit to [winget-pkgs](https://github.com/microsoft/winget-pkgs) after each `v*` release |
| **Scoop** (extras-style JSON) | Manifest under `scoop/` — add to a personal/community bucket |
| **Chocolatey** | Skeleton under `chocolatey/` — publish when a package maintainer is ready |
| **MSIX** | **Deferred** — needs Assets, identity, and signing; incomplete manifests were removed on purpose |
| **Inno Setup** | Optional wrapper script under `inno/` for a classic Setup.exe |

Do not re-introduce empty `Package.appxmanifest` files without a complete MSIX asset set.

## Release artifact

Tag `vX.Y.Z` → workflow uploads:

- `DiskUtilities-win-x64.zip` — suite binaries (one folder per app; start `SuiteLauncher\SuiteLauncher.exe`)
- `SHA256SUMS.txt` — checksum for the zip

Install layout after extract: run `SuiteLauncher\SuiteLauncher.exe` (optional: Install Explorer menus).

## Fill manifests for a release

```powershell
# From repo root, after a GitHub Release exists:
.\packaging\New-ManifestsFromTag.ps1 -Version 1.2.3 -ZipUrl "https://github.com/PWagner1/Disk-Utilities/releases/download/v1.2.3/DiskUtilities-win-x64.zip"
```

Writes filled copies under `artifacts/manifests/` (gitignored build output). Copy those into a winget-pkgs PR or Scoop bucket.

## winget

1. Run `New-ManifestsFromTag.ps1` for the tag.
2. Validate: `winget validate artifacts\manifests\winget`
3. Open a PR against [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) under `manifests/p/PWagner1/DiskUtilities/<version>/`.

Package id: `PWagner1.DiskUtilities`.

## Scoop

Add `artifacts/manifests/scoop/disk-utilities.json` to a bucket, or:

```powershell
scoop bucket add <your-bucket> <url>
scoop install disk-utilities
```

## Chocolatey

Skeleton only — fill version/URL/checksum, then `choco pack` / `choco push` from `chocolatey/`.

## Inno Setup (optional)

Open `inno/DiskUtilities.iss` in [Inno Setup](https://jrsoftware.org/isinfo.php) after extracting a Release zip next to the script (see comments in the `.iss` file). Produces `DiskUtilities-Setup-x64.exe`.
