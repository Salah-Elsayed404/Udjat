# Builds the Udjat installer: publishes the app (self-contained, Release), then runs Inno Setup on installer\Udjat.iss.
# The version comes from src\Udjat\Udjat.csproj, so there is one place to change it.
# Result: artifacts\Udjat-Setup-<version>.exe
#   powershell -File tools\build-installer.ps1
param([string]$Iscc = '')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if (-not $Iscc) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'))
    $Iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Iscc) { throw 'Inno Setup 6 (ISCC.exe) was not found. Install it from https://jrsoftware.org/isinfo.php, or pass -Iscc <path>.' }

$project = Join-Path $root 'src\Udjat\Udjat.csproj'
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> in $project" }

$publish = Join-Path $root 'artifacts\publish'
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }   # no leftovers from an older publish

Write-Host "Publishing Udjat $version ..."
dotnet publish $project -c Release -r win-x64 --self-contained true -p:DebugType=none -p:DebugSymbols=false -o $publish   # no .pdb files in the installer
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Write-Host "Building the installer with $Iscc ..."
& $Iscc "/DAppVersion=$version" (Join-Path $root 'installer\Udjat.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }

Get-Item (Join-Path $root "artifacts\Udjat-Setup-$version.exe") | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
