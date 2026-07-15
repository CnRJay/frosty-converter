#Requires -Version 5.1
<#
.SYNOPSIS
  Build release zip packages for MMC plugin and FIFA CLI tool.

.EXAMPLE
  .\scripts\pack-release.ps1
  .\scripts\pack-release.ps1 -Version 1.2.0 -MmcEditorDir "D:\cfb27 mods\MMC_Editor_v1.1.0.0"
#>
param(
    [string]$Version = "",
    [string]$Configuration = "Release",
    [switch]$SkipPlugin,
    [switch]$SkipCli,
    [string]$MmcEditorDir = ""
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $Root

if (-not $Version) {
    $gitTag = ""
    try { $gitTag = (& git describe --tags --exact-match 2>$null) } catch { }
    if ($gitTag) {
        $Version = $gitTag.Trim().TrimStart("v")
    } else {
        $Version = "1.0.0"
    }
}

$Dist = Join-Path $Root "dist"
$Staging = Join-Path $Dist "_staging"
if (Test-Path $Dist) { Remove-Item $Dist -Recurse -Force }
New-Item -ItemType Directory -Path $Staging -Force | Out-Null

$Oodle = Join-Path $Root "third_party\oodle\bin\oodle-data-shared.dll"
if (-not (Test-Path $Oodle)) {
    throw "Missing $Oodle - required for release packages."
}

$artifacts = @()

# ---------------------------------------------------------------------------
# FIFA / CLI + GUI tool (self-contained win-x64)
# ---------------------------------------------------------------------------
if (-not $SkipCli) {
    Write-Host "==> Publishing CLI (self-contained win-x64)..." -ForegroundColor Cyan
    $cliOut = Join-Path $Staging "FifaTool"
    New-Item -ItemType Directory -Path $cliOut -Force | Out-Null

    & dotnet publish (Join-Path $Root "src\FrostyConvert.Cli\FrostyConvert.Cli.csproj") `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:Version=$Version `
        -o $cliOut
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish CLI failed" }

    Write-Host "==> Publishing FIFA GUI (self-contained win-x64)..." -ForegroundColor Cyan
    $guiTmp = Join-Path $Staging "FifaGui_publish"
    if (Test-Path $guiTmp) { Remove-Item $guiTmp -Recurse -Force }
    & dotnet publish (Join-Path $Root "src\FrostyConvert.FifaGui\FrostyConvert.FifaGui.csproj") `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:Version=$Version `
        -o $guiTmp
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish FIFA GUI failed" }
    Copy-Item (Join-Path $guiTmp "FrostyConvert.FifaGui.exe") $cliOut -Force

    # Keep only: single-file exes + native Oodle + INSTALL
    Get-ChildItem $cliOut -File | Where-Object {
        $_.Name -notin @("fbmod2project.exe", "FrostyConvert.FifaGui.exe", "INSTALL.txt") -and
        $_.Extension -ne ".exe"
    } | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem $cliOut -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem $cliOut -Filter *.json -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem $cliOut -Filter *.dll -ErrorAction SilentlyContinue | Remove-Item -Force

    Copy-Item $Oodle $cliOut -Force
    Copy-Item (Join-Path $Root "packaging\FifaTool\INSTALL.txt") $cliOut -Force

    $cliZip = Join-Path $Dist "FrostyConvert-FifaTool-v$Version-win-x64.zip"
    Compress-Archive -Path (Join-Path $cliOut "*") -DestinationPath $cliZip -Force
    $artifacts += $cliZip
    Write-Host "    Created $cliZip" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# MMC plugin
# ---------------------------------------------------------------------------
if (-not $SkipPlugin) {
    Write-Host "==> Building MMC plugin..." -ForegroundColor Cyan
    $pluginProj = Join-Path $Root "src\FrostyConvert.MmcPlugin\FrostyConvert.MmcPlugin.csproj"

    $pluginBuildArgs = @(
        "build", $pluginProj,
        "-c", $Configuration,
        "-p:Version=$Version",
        "-p:SkipMmcDeploy=true"
    )
    if ($MmcEditorDir) {
        $pluginBuildArgs += "-p:MmcEditorDir=$MmcEditorDir"
    }
    & dotnet @pluginBuildArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet build MMC plugin failed (exit $LASTEXITCODE)" }

    $pluginBin = Join-Path $Root "src\FrostyConvert.MmcPlugin\bin\$Configuration"
    $pluginDll = Join-Path $pluginBin "FrostyConvert.MmcPlugin.dll"
    if (-not (Test-Path $pluginDll)) {
        throw "Plugin DLL not found at $pluginDll"
    }

    $pluginStage = Join-Path $Staging "MmcPlugin"
    $pluginsDir = Join-Path $pluginStage "Plugins"
    New-Item -ItemType Directory -Path $pluginsDir -Force | Out-Null

    # Costura merges managed deps into one DLL
    Copy-Item $pluginDll $pluginsDir -Force

    Copy-Item $Oodle $pluginStage -Force
    Copy-Item (Join-Path $Root "packaging\MmcPlugin\INSTALL.txt") $pluginStage -Force

    $pluginZip = Join-Path $Dist "FrostyConvert-MmcPlugin-v$Version.zip"
    Compress-Archive -Path (Join-Path $pluginStage "*") -DestinationPath $pluginZip -Force
    $artifacts += $pluginZip
    Write-Host "    Created $pluginZip" -ForegroundColor Green
}

Write-Host ""
Write-Host "Release packages (v$Version):" -ForegroundColor Cyan
foreach ($a in $artifacts) {
    $item = Get-Item $a
    $kb = [math]::Round($item.Length / 1KB, 1)
    Write-Host ("  {0}  ({1} KB)" -f $item.Name, $kb)
}
Write-Host "Output directory: $Dist"
