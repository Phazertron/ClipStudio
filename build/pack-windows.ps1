<#
.SYNOPSIS
    Build and package ClipStudio for Windows using Velopack.

.DESCRIPTION
    Publishes the ClipStudio.UI project as a self-contained Windows x64 application,
    then invokes the Velopack CLI (vpk) to produce a Windows Setup installer and
    a delta-update package.

    Prerequisites
    -------------
    - .NET 9 SDK
    - vpk CLI installed:  dotnet tool install -g vpk
    - (Optional) A code-signing certificate configured via the -SigningCertThumb parameter.

.PARAMETER Version
    The semantic version string for this release (e.g. "1.0.0", "1.2.3").
    Defaults to "1.0.0".

.PARAMETER ReleaseDir
    The directory where Velopack will write the installer and update packages.
    Defaults to ".\releases\windows".

.PARAMETER Channel
    The Velopack release channel ("stable", "beta", "nightly").
    Defaults to "stable".

.PARAMETER SigningCertThumb
    Optional SHA-1 thumbprint of a Windows code-signing certificate available in
    the current user's certificate store.  When provided, the installer and the
    application executable are signed.

.EXAMPLE
    .\build\pack-windows.ps1 -Version "1.0.0"
    .\build\pack-windows.ps1 -Version "1.1.0" -Channel "beta" -SigningCertThumb "ABCDEF..."
#>

param (
    [string] $Version            = "1.0.0",
    [string] $ReleaseDir         = ".\releases\windows",
    [string] $Channel            = "stable",
    [string] $SigningCertThumb   = "",
    # Set to $true to skip the FFmpeg download (e.g. if already cached or not needed).
    [switch] $SkipFfmpegDownload = $false
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$ProjectDir = Join-Path $RepoRoot "src\ClipStudio.UI"
$PublishDir = Join-Path $RepoRoot "publish\windows"

Write-Host "=== ClipStudio Windows Packager ===" -ForegroundColor Cyan
Write-Host "Version    : $Version"
Write-Host "Channel    : $Channel"
Write-Host "ReleaseDir : $ReleaseDir"
Write-Host ""

# ---------------------------------------------------------------------------
# Step 1: Restore & publish
# ---------------------------------------------------------------------------
Write-Host "[1/4] Publishing ClipStudio.UI..." -ForegroundColor Yellow

if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

dotnet publish "$ProjectDir\ClipStudio.UI.csproj" `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output "$PublishDir" `
    -p:Version="$Version" `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# Copy OBS scripts into the publish output so they are included in the installer.
$ObsScriptsSrc = Join-Path $RepoRoot "obs-scripts"
$ObsScriptsDst = Join-Path $PublishDir "obs-scripts"
if (Test-Path $ObsScriptsSrc) {
    Copy-Item $ObsScriptsSrc -Destination $ObsScriptsDst -Recurse -Force
    Write-Host "Copied OBS scripts to publish output."
}

# ---------------------------------------------------------------------------
# Step 2: Bundle FFmpeg (LGPL) so end users need no separate installation.
# Binaries are placed in <PublishDir>/ffmpeg/ which ConfigureFfmpeg() in
# App.axaml.cs auto-detects at startup.
# Source: BtbN FFmpeg-Builds (GitHub) — LGPL-licensed essentials build.
# ---------------------------------------------------------------------------
Write-Host "[2/4] Bundling FFmpeg LGPL essentials..." -ForegroundColor Yellow

if ($SkipFfmpegDownload) {
    Write-Host "  Skipping FFmpeg download (-SkipFfmpegDownload specified)."
} else {
    $FfmpegDir     = Join-Path $PublishDir "ffmpeg"
    $FfmpegZipUrl  = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
    $FfmpegZip     = Join-Path $env:TEMP "clipstudio-ffmpeg-bundle.zip"
    $FfmpegExtract = Join-Path $env:TEMP "clipstudio-ffmpeg-extract"

    if (-not (Test-Path $FfmpegDir)) { New-Item -ItemType Directory -Path $FfmpegDir | Out-Null }

    Write-Host "  Downloading $FfmpegZipUrl ..."
    Invoke-WebRequest -Uri $FfmpegZipUrl -OutFile $FfmpegZip -UseBasicParsing

    Write-Host "  Extracting ..."
    if (Test-Path $FfmpegExtract) { Remove-Item $FfmpegExtract -Recurse -Force }
    Expand-Archive -Path $FfmpegZip -DestinationPath $FfmpegExtract -Force

    # The zip contains a top-level versioned folder (e.g. ffmpeg-master-.../bin/).
    $BinDir = Get-ChildItem $FfmpegExtract -Recurse -Directory |
              Where-Object { $_.Name -eq "bin" } |
              Select-Object -First 1

    if ($BinDir) {
        foreach ($exe in @("ffmpeg.exe", "ffprobe.exe")) {
            $src = Join-Path $BinDir.FullName $exe
            if (Test-Path $src) {
                Copy-Item $src -Destination $FfmpegDir -Force
                Write-Host "  Bundled $exe"
            } else {
                Write-Warning "  $exe not found in archive bin/ folder."
            }
        }
    } else {
        Write-Warning "  Could not locate bin/ folder inside FFmpeg archive. FFmpeg was NOT bundled."
    }

    Remove-Item $FfmpegZip     -Force -ErrorAction SilentlyContinue
    Remove-Item $FfmpegExtract -Recurse -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------------------
# Step 3: Ensure the Velopack CLI tool is available
# ---------------------------------------------------------------------------
Write-Host "[3/4] Checking vpk CLI..." -ForegroundColor Yellow

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host "vpk not found - installing globally..."
    dotnet tool install -g vpk
    if ($LASTEXITCODE -ne 0) { throw "Failed to install vpk." }
}

# ---------------------------------------------------------------------------
# Step 3: Pack with Velopack
# ---------------------------------------------------------------------------
Write-Host "[4/4] Packing installer..." -ForegroundColor Yellow

if (-not (Test-Path $ReleaseDir)) { New-Item -ItemType Directory -Path $ReleaseDir | Out-Null }

$vpkArgs = @(
    "pack",
    "--packId",      "ClipStudio",
    "--packVersion", $Version,
    "--packDir",     $PublishDir,
    "--mainExe",     "ClipStudio.UI.exe",
    "--outputDir",   $ReleaseDir,
    "--channel",     $Channel,
    "--packTitle",   "ClipStudio",
    "--packAuthors", "ClipStudio Team"
)

# Include an application icon when one is present.
# Replace Assets\clipstudio.ico with a proper icon before publishing.
$IconPath = Join-Path $ProjectDir "Assets\clipstudio.ico"
if (Test-Path $IconPath) {
    $vpkArgs += "--icon"
    $vpkArgs += $IconPath
} else {
    Write-Warning "No clipstudio.ico found in Assets\ - packing without a custom icon."
}

if ($SigningCertThumb) {
    $vpkArgs += "--signParams"
    $vpkArgs += "/a /fd sha256 /tr http://timestamp.sectigo.com /td sha256 /sha1 $SigningCertThumb"
}

& vpk @vpkArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed." }

Write-Host ""
Write-Host "=== Package complete ===" -ForegroundColor Green
Write-Host "Artifacts: $ReleaseDir"
Get-ChildItem $ReleaseDir | Select-Object Name, Length | Format-Table -AutoSize
