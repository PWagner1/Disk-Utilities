#Requires -Version 5.1
<#
.SYNOPSIS
  Fills winget / Scoop / Chocolatey templates for a GitHub Release tag.
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $ZipUrl,

    [string] $Sha256 = '',

    [string] $ZipPath = '',

    [string] $OutDir = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
if (-not $OutDir) {
    $OutDir = Join-Path $repoRoot 'artifacts\manifests'
}

$Version = $Version.TrimStart('v')
if (-not $Sha256) {
    if (-not $ZipPath) {
        $tmp = Join-Path $env:TEMP ("DiskUtilities-" + $Version + ".zip")
        Write-Host "Downloading $ZipUrl ..."
        Invoke-WebRequest -Uri $ZipUrl -OutFile $tmp -UseBasicParsing
        $ZipPath = $tmp
    }
    $Sha256 = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Set-Placeholders([string] $text) {
    return $text.
        Replace('0.0.0', $Version).
        Replace('v0.0.0', "v$Version").
        Replace('https://github.com/PWagner1/Disk-Utilities/releases/download/v0.0.0/DiskUtilities-win-x64.zip', $ZipUrl).
        Replace('0000000000000000000000000000000000000000000000000000000000000000', $Sha256)
}

$wingetOut = Join-Path $OutDir "winget\PWagner1.DiskUtilities\$Version"
$scoopOut = Join-Path $OutDir 'scoop'
$chocoOut = Join-Path $OutDir 'chocolatey'
New-Item -ItemType Directory -Force -Path $wingetOut, $scoopOut, (Join-Path $chocoOut 'tools') | Out-Null

foreach ($name in @(
        'PWagner1.DiskUtilities.yaml',
        'PWagner1.DiskUtilities.installer.yaml',
        'PWagner1.DiskUtilities.locale.en-US.yaml'
    )) {
    $src = Join-Path $PSScriptRoot "winget\$name"
    $dst = Join-Path $wingetOut $name
    Set-Placeholders (Get-Content -Raw $src) | Set-Content -Path $dst -Encoding utf8
}

Set-Placeholders (Get-Content -Raw (Join-Path $PSScriptRoot 'scoop\disk-utilities.json')) |
    Set-Content -Path (Join-Path $scoopOut 'disk-utilities.json') -Encoding utf8

Set-Placeholders (Get-Content -Raw (Join-Path $PSScriptRoot 'chocolatey\disk-utilities.nuspec')) |
    Set-Content -Path (Join-Path $chocoOut 'disk-utilities.nuspec') -Encoding utf8
Set-Placeholders (Get-Content -Raw (Join-Path $PSScriptRoot 'chocolatey\tools\chocolateyInstall.ps1')) |
    Set-Content -Path (Join-Path $chocoOut 'tools\chocolateyInstall.ps1') -Encoding utf8

$iss = Get-Content -Raw (Join-Path $PSScriptRoot 'inno\DiskUtilities.iss')
$iss = $iss -replace '#define MyAppVersion "0.0.0"', "#define MyAppVersion `"$Version`""
$issOut = Join-Path $OutDir 'inno'
New-Item -ItemType Directory -Force -Path $issOut | Out-Null
Set-Content -Path (Join-Path $issOut 'DiskUtilities.iss') -Value $iss -Encoding utf8

Write-Host "Wrote manifests to $OutDir"
Write-Host "SHA256: $Sha256"
