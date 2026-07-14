# Oodle data library

Frostbite mods (CFB, Madden, EA FC, and others) often compress asset payloads with **Oodle**. FrostyConvert needs an `OodleLZ_Decompress` implementation at runtime.

## What ships here

| Path | Source |
|------|--------|
| `bin/oodle-data-shared.dll` | [WorkingRobot/OodleUE](https://github.com/WorkingRobot/OodleUE) `msvc-x64-release` build of Epic’s **Oodle-for-Unreal-Engine** data library |

This is **not** a reverse-engineered `oo2core` DLL. It is the Oodle *data* library as built for Unreal/OodleUE, with a verified `OodleLZ_Decompress` export used for:

- CAS-compressed `.fbmod` payloads (e.g. College Football / Madden)
- CAS type **0x19** (Leviathan family) streams in `.fifamod` / recovered `.fifaproject`

The CLI and test projects copy this DLL next to the build output when present.

## Refresh / re-download

```bat
curl -sL -o msvc-x64-release.zip https://github.com/WorkingRobot/OodleUE/releases/download/2026-06-04-1357/msvc-x64-release.zip
tar -xf msvc-x64-release.zip bin/oodle-data-shared.dll
```

Keep the zip out of git (see root `.gitignore`); only `bin/oodle-data-shared.dll` is expected in the tree.

## Alternatives

| Option | How |
|--------|-----|
| Explicit path | CLI: `--oodle path\to\oodle-data-shared.dll` (or `oo2core_*_win64.dll` from a game/toolsuite) |
| Managed fallback | **OozSharp** (NuGet) — pure managed Kraken; incomplete for some Frostbite streams |

## MMC plugin

When building `FrostyConvert.MmcPlugin`, the same DLL is deployed next to MMC Editor so the import path can decompress Oodle blocks without a separate install.

## License

Oodle is a product of RAD Game Tools / Epic Games. Use of `oodle-data-shared.dll` follows Epic’s Unreal Engine / OodleUE terms. Do not treat this as a free redistributable of the proprietary Oodle SDK; keep the UE/OodleUE provenance when documenting or packaging.
