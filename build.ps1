<#
.SYNOPSIS
  Builds Spotlight and produces the release artefacts in .\dist

.DESCRIPTION
  dist\Spotlight_<version>.zip   the app package. Attach THIS to the GitHub release; the in-app updater
                                 and SpotlightSetup.exe both download it.
  dist\SpotlightSetup.exe        small first-time installer (self-contained, downloads the latest release,
                                 or installs a Spotlight_*.zip placed next to it).

  Requires the .NET 9 SDK and Node.js 18+.
#>
param(
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$appProj = Join-Path $root "src\Spotlight.App\Spotlight.App.csproj"
$setupProj = Join-Path $root "src\Spotlight.Setup\Spotlight.Setup.csproj"
$dist = Join-Path $root "dist"
$stage = Join-Path $dist "stage"

# Antivirus scanners sometimes hold a freshly written exe for a moment; retry instead of failing the release.
function Invoke-Publish {
    param([string[]]$Arguments)
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        & dotnet publish @Arguments
        if ($LASTEXITCODE -eq 0) { return }
        if ($attempt -lt 4) { Write-Host "Publish failed (attempt $attempt); retrying..." -ForegroundColor Yellow; Start-Sleep -Seconds 4 }
    }
    throw "dotnet publish failed: $($Arguments -join ' ')"
}

[xml]$xml = Get-Content $appProj
$version = ($xml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
Write-Host "Building Spotlight $version" -ForegroundColor Cyan

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

Write-Host "`n[1/4] Installing UI dependencies" -ForegroundColor Cyan
Push-Location (Join-Path $root "src\Spotlight.UI")
if (-not (Test-Path "node_modules")) { npm ci }
Pop-Location

Write-Host "`n[2/4] Publishing app (builds the React UI too)" -ForegroundColor Cyan
Invoke-Publish @($appProj, "-c", "Release", "-r", "win-x64", "--self-contained", "false", "-p:SkipSetupBuild=true", "-o", $stage)

Write-Host "`n[3/4] Publishing updater (Spotlight.Setup.exe, framework-dependent, ships inside the zip)" -ForegroundColor Cyan
Invoke-Publish @($setupProj, "-c", "Release", "-r", "win-x64", "--self-contained", "false", "-o", $stage)

# Only the files the app needs at runtime
Get-ChildItem $stage -Include *.pdb, *.xml -Recurse | Remove-Item -Force
if (-not (Test-Path (Join-Path $stage "ui\index.html"))) { throw "UI files are missing from the package" }

$zip = Join-Path $dist "Spotlight_$version.zip"
# .NET writes forward-slash entry names (Compress-Archive on Windows PowerShell 5.1 does not).
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host "Package: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)" -ForegroundColor Green

if (-not $SkipInstaller) {
    Write-Host "`n[4/4] Publishing first-time installer" -ForegroundColor Cyan
    $installerOut = Join-Path $dist "installer"
    Invoke-Publish @($setupProj, "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:PublishSingleFile=true",
        "-p:EnableCompressionInSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-o", $installerOut)
    Copy-Item (Join-Path $installerOut "Spotlight.Setup.exe") (Join-Path $dist "SpotlightSetup.exe")
    Remove-Item $installerOut -Recurse -Force
}

Remove-Item $stage -Recurse -Force
Write-Host "`nDone. Upload dist\Spotlight_$version.zip to a GitHub release tagged v$version." -ForegroundColor Green
