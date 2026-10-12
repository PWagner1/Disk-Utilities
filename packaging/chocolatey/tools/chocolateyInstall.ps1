$ErrorActionPreference = 'Stop'
$packageName = 'disk-utilities'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
# Filled by New-ManifestsFromTag.ps1 or maintainer before choco pack:
$url64 = 'https://github.com/PWagner1/Disk-Utilities/releases/download/v0.0.0/DiskUtilities-win-x64.zip'
$checksum64 = '0000000000000000000000000000000000000000000000000000000000000000'

$packageArgs = @{
  packageName    = $packageName
  unzipLocation  = $toolsDir
  url64bit       = $url64
  checksum64     = $checksum64
  checksumType64 = 'sha256'
}

Install-ChocolateyZipPackage @packageArgs

$launcher = Join-Path $toolsDir 'SuiteLauncher.exe'
if (Test-Path $launcher) {
  Install-ChocolateyShortcut -ShortcutFilePath (
    Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Disk Utilities.lnk'
  ) -TargetPath $launcher
}
