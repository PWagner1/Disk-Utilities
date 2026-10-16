# Disk Health

WinUI 3 tool for **signature carving** recovery and **S.M.A.R.T. / NVMe** drive health.

## Features

- **Signature carving** — Scans raw volume data for known headers (JPEG, PNG, PDF, GIF, ZIP/Office XML, 7z, RAR, WebP, HEIC, SQLite, PE, ISO, MKV, FLAC, MP4, and more). Kept as its own recovery mode.
- **Recycle Bin undelete** — Lists `$Recycle.Bin` `$I`/`$R` pairs and restores the `$R` payload with the original name
- **MFT deleted (scoped)** — Scans NTFS Master File Table for unused FILE records with residual names/data runs (clusters may already be reused)
- **Optional volume image** — Read-only raw `.img`/`.dd` copy (first 8 GB or entire volume) before scanning
- **Quick vs deep carve** — Quick limits to the first 8 GB; deep scans the full volume
- **Pause / resume / cancel** — Control long carves without losing progress so far
- **Type filter & preview** — Filter candidates by extension; preview images and PDF headers before recover
- **Export** — Save the candidate list as CSV or JSON
- **S.M.A.R.T. stats** — ATA attributes via WMI; NVMe Health Information Log (temperature, available spare, percentage used)

## Safety

- Administrator rights are required for carve, MFT, and volume imaging (Recycle Bin restore usually works without raw IO)
- The UI labels each candidate as **Carve**, **Recycle Bin**, or **MFT deleted** — do not treat carving as undelete
- Recovery chance scores are **heuristic estimates**, not guarantees
- Carved / MFT results may be incomplete, false positives, or partially overwritten; Recycle Bin restores are the most reliable when `$R` still exists

## Building

```powershell
cd Source
dotnet build DiskHealth\DiskHealth.csproj -c Debug -p:Platform=x64
dotnet run --project DiskHealth -p:Platform=x64
```

Executable: `bin\x64\Debug\net10.0-windows10.0.19041.0\DiskHealth\DiskHealth.exe` (repo root).

## Related

- Suite overview: [Source/README.md](../README.md)
- Root README: [Disk Utilities](../../README.md)
