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
# FIFA / CLI tool (self-contained win-x64)
# ---------------------------------------------------------------------------
if (-not $SkipCli) {
    Write-Host "==> Publishing CLI (self-contained win-x64)..." -ForegroundColor Cyan
    $cliOut = Join-Path $Staging "FifaTool"
    New-Item -ItemType Directory -Path $cliOut -Force | Out-Null

    & dotnet publish (Join-Path $Root "src\FrostyConvert.Cli\FrostyConvert.Cli.csproj") `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:Version=$Version `
        -o $cliOut
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish CLI failed" }

    Copy-Item $Oodle $cliOut -Force
    Copy-Item (Join-Path $Root "packaging\FifaTool\INSTALL.txt") $cliOut -Force
    Get-ChildItem $cliOut -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

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

    if ($MmcEditorDir) {
        & dotnet build $pluginProj -c $Configuration -p:Version=$Version -p:MmcEditorDir=$MmcEditorDir
    } else {
        & dotnet build $pluginProj -c $Configuration -p:Version=$Version
    }
    if ($LASTEXITCODE -ne 0) { throw "dotnet build MMC plugin failed" }

    $pluginBin = Join-Path $Root "src\FrostyConvert.MmcPlugin\bin\$Configuration"
    $pluginDll = Join-Path $pluginBin "FrostyConvert.MmcPlugin.dll"
    if (-not (Test-Path $pluginDll)) {
        throw "Plugin DLL not found at $pluginDll"
    }

    $pluginStage = Join-Path $Staging "MmcPlugin"
    $pluginsDir = Join-Path $pluginStage "Plugins"
    New-Item -ItemType Directory -Path $pluginsDir -Force | Out-Null

    $pluginFiles = @(
        "FrostyConvert.MmcPlugin.dll",
        "K4os.Compression.LZ4.dll",
        "ZstdSharp.dll",
        "System.Buffers.dll",
        "System.Memory.dll",
        "System.Numerics.Vectors.dll",
        "System.Runtime.CompilerServices.Unsafe.dll",
        "System.Threading.Tasks.Extensions.dll"
    )
    foreach ($f in $pluginFiles) {
        $src = Join-Path $pluginBin $f
        if (Test-Path $src) {
            Copy-Item $src $pluginsDir -Force
        }
    }

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
