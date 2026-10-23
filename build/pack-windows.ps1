# Builds a Windows release of KHost with Velopack: KHost-win-Setup.exe, the full (and delta) packages and
# releases.win.json, in one folder that is the update feed (upload its contents to the release host).
# The pack id is KHost.App, not KHost: Velopack owns %LocalAppData%\<packId> outright (an install or reinstall
# replaces it, an uninstall deletes it), and %LocalAppData%\KHost is where KHost and its plugins keep their data.
# Usage: build/pack-windows.ps1 -Version 2.1.0 [-Output artifacts/velopack] [-PreviousReleases <dir>]
# Needs: the vpk tool at Velopack's version (dotnet tool install -g vpk --version 1.2.161) and Oriel in the
# local feed (nuget.config). Unsigned: SmartScreen warns on first run until a signing certificate is wired in.
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Output = (Join-Path $PSScriptRoot '..' 'artifacts' 'velopack'),
    # A folder holding earlier releases' packages and releases.win.json; vpk builds a delta against the newest.
    [string] $PreviousReleases
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$publish = Join-Path $root 'artifacts' 'publish' 'win-x64'
$Output = [IO.Path]::GetFullPath($Output)

# A fresh publish folder: leftovers from an earlier layout (a renamed exe, a stale marker) would ship.
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
dotnet publish (Join-Path $root 'src' 'KHost.UserInterface' 'KHost.UserInterface.csproj') `
    -c Release -r win-x64 --self-contained -o $publish -p:Version=$Version -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
foreach ($file in 'KHost.exe', 'KHost.Screen.exe', 'KHost.oriel-app', 'KHost.Screen.oriel-app') {
    if (-not (Test-Path (Join-Path $publish $file))) { throw "$file is missing from the publish: is Oriel's layout in the feed?" }
}

New-Item -ItemType Directory -Force $Output | Out-Null
if ($PreviousReleases) { Copy-Item (Join-Path $PreviousReleases '*') $Output -Force }

vpk pack --packId KHost.App --packVersion $Version --runtime win-x64 --packDir $publish --mainExe KHost.exe `
    --packTitle KHost --packAuthors riddlemd --icon (Join-Path $root 'assets' 'icon' 'khost.ico') `
    --outputDir $Output
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed ($LASTEXITCODE)" }
Get-ChildItem $Output | Select-Object Name, Length
