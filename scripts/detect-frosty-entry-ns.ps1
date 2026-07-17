# Prints "Entries" if FrostySdk has FrostySdk.Managers.Entries.AssetEntry (MMC 1.1+),
# otherwise "Legacy" (stock FrostyToolsuite: types live in FrostySdk.Managers).
param(
    [Parameter(Mandatory = $true)]
    [string] $SdkPath
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath $SdkPath)) {
    Write-Error "FrostySdk not found: $SdkPath"
    exit 2
}

$full = (Resolve-Path -LiteralPath $SdkPath).Path
$asm = [System.Reflection.Assembly]::LoadFrom($full)
if ($null -ne $asm.GetType("FrostySdk.Managers.Entries.AssetEntry", $false)) {
    Write-Output "Entries"
} else {
    Write-Output "Legacy"
}
