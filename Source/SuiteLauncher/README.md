# SuiteLauncher

WinUI 3 entry point for the **Disk Utilities** suite. Lists the five tools with short descriptions and elevation hints, seeds suite theme / destructive-prompt preference, and launches sibling executables from the shared `bin\` output folder.

## Features

- Launch DiskEraser, DiskHealth, DiskSeer, DriveStat, and HandleFinder
- Optional **Launch elevated** (UAC) per tool
- Suite theme (system / light / dark) stored in `%LocalAppData%\DiskUtilities\suite-settings.json`
- Preference for acknowledgement checkboxes on destructive ops (apps still always confirm)
- **Install / Remove Explorer menus** (per-user HKCU): “What has this locked?” → HandleFinder; “Analyze disk usage” → DriveStat  
  CLI: `SuiteLauncher.exe --install-shell` / `--uninstall-shell`

## Build and run

```powershell
cd Source
dotnet build SuiteLauncher\SuiteLauncher.csproj -c Debug -p:Platform=x64 -m:1
dotnet run --project SuiteLauncher -p:Platform=x64
```

Build the other apps into the same output folder so Launch can find them:

`bin\x64\Debug\net10.0-windows10.0.19041.0\`

## Related

- Suite overview: [Source/README.md](../README.md)
- Shared helpers: `SuiteApps`, `SuiteSettings` in DiskUtilities.Shared
