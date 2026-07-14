$ErrorActionPreference = "Stop"
$zip = Join-Path $env:TEMP "FrostyEditor.zip"
$extract = Join-Path $env:TEMP "FrostyEditorExtract"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -ErrorAction SilentlyContinue
if (-not $root) { $root = Resolve-Path (Join-Path $PSScriptRoot "..") }
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$refs = Join-Path $root "third_party\mmc-refs"

if (-not (Test-Path $zip)) {
    Write-Host "Downloading FrostyEditor.zip..."
    Invoke-WebRequest -Uri "https://github.com/CadeEvs/FrostyToolsuite/releases/download/v1.0.6.3/FrostyEditor.zip" -OutFile $zip
}

if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
Expand-Archive -Path $zip -DestinationPath $extract -Force
New-Item -ItemType Directory -Path $refs -Force | Out-Null

$core = Get-ChildItem $extract -Recurse -Filter "FrostyCore.dll" | Select-Object -First 1
$sdk = Get-ChildItem $extract -Recurse -Filter "FrostySdk.dll" | Select-Object -First 1
$ctrl = Get-ChildItem $extract -Recurse -Filter "FrostyControls.dll" | Select-Object -First 1
if (-not $core -or -not $sdk -or -not $ctrl) { throw "Missing Frosty DLLs in zip" }

Copy-Item $core.FullName (Join-Path $refs "FrostyCore.dll") -Force
Copy-Item $sdk.FullName (Join-Path $refs "FrostySdk.dll") -Force
Copy-Item $ctrl.FullName (Join-Path $refs "FrostyControls.dll") -Force
Write-Host "OK: $refs"
Get-ChildItem $refs | ForEach-Object { Write-Host ("  {0} {1}" -f $_.Name, $_.Length) }
