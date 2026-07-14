# Oodle data library (for FrostyConvert)

Frostbite mods (especially Madden / College Football) compress EBX with **Oodle**.

## What we ship

| File | Source |
|------|--------|
| `bin/oodle-data-shared.dll` | [WorkingRobot/OodleUE](https://github.com/WorkingRobot/OodleUE) `msvc-x64-release` build of Epic’s **Oodle-for-Unreal-Engine** data library |

This is **not** reverse-engineered `oo2core`. It is the Oodle data compression library as distributed for Unreal Engine / OodleUE builds, exposing `OodleLZ_Decompress` (verified against CFB `.fbmod` samples).

## Refresh / re-download

```bat
curl -sL -o msvc-x64-release.zip https://github.com/WorkingRobot/OodleUE/releases/download/2026-06-04-1357/msvc-x64-release.zip
tar -xf msvc-x64-release.zip bin/oodle-data-shared.dll
```

## Alternatives

- Game/toolsuite `oo2core_*_win64.dll` via CLI `--oodle path\to\dll`
- Managed fallback: **OozSharp** (open Kraken reimplementation) — incomplete for some Frostbite streams

## License note

Oodle is a product of RAD Game Tools / Epic Games. Use of `oodle-data-shared.dll` is subject to Epic’s Unreal Engine / Oodle licensing terms. Do not treat this as a free-for-all redistributable of the proprietary Oodle SDK; keep the UE/OodleUE provenance.
