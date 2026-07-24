# Frosty Converter

[![CI](https://github.com/CnRJay/frosty-converter/actions/workflows/ci.yml/badge.svg)](https://github.com/CnRJay/frosty-converter/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/CnRJay/frosty-converter?include_prereleases&sort=semver)](https://github.com/CnRJay/frosty-converter/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/CnRJay/frosty-converter/total)](https://github.com/CnRJay/frosty-converter/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Recover **editable editor projects** from compiled Frostbite mods when the original project is gone.

| Input | Output | Tool |
|-------|--------|------|
| **`.fifamod`** (FIFA Editor Tool) | **`.fifaproject`** | `Frosty Converter.exe` (GUI or CLI) |
| **`.fbmod`** (Frosty / MMC) | Live import → **`.fbproject`** | MMC Editor plugin |

## Download

Get the latest packages from **[Releases](https://github.com/CnRJay/frosty-converter/releases/latest)**:

| Package | For |
|---------|-----|
| **`FrostyConvert-FifaTool-*-win-x64.zip`** | EA FC / FIFA — `.fifamod` → `.fifaproject` |
| **`FrostyConvert-MmcPlugin-*.zip`** | College Football / Madden — import `.fbmod` in MMC |

### Windows SmartScreen / antivirus

Release builds are **not code-signed**. Windows or antivirus software may warn that `Frosty Converter.exe` is unknown or flag it as malware — that is a **false positive** common with unsigned open-source tools.

- Prefer **More info → Run anyway** (SmartScreen), or add an exclusion for the unzip folder.
- Or build from source (below) if you want a binary you compiled yourself.

Keep `Frosty Converter.exe` and `oodle-data-shared.dll` in the **same folder**.

## FIFA / EA FC (`.fifamod`)

1. Unzip the **FifaTool** package.
2. Double-click **`Frosty Converter.exe`**, pick a `.fifamod`, click **Convert**.
3. Open **FIFA Editor Tool**, load the matching game (e.g. FC26).
4. **File → Open Project** → the recovered `.fifaproject`.
5. **File → Save**, then export a **new** `.fifamod` and test it.

**CLI** (same exe — pass arguments):

```bat
"Frosty Converter.exe" "mod.fifamod" -o recovered.fifaproject
"Frosty Converter.exe" "mod.fifamod" --inspect
"Frosty Converter.exe" --help
```

More detail: [docs/fifa-import.md](docs/fifa-import.md).

## College Football / Madden (`.fbmod`)

CFB/Madden gameplay EBX is **RIFF** — prefer the **MMC plugin** over offline `.fbproject` files.

1. Close MMC Editor.
2. Copy `Plugins\FrostyConvert.MmcPlugin.dll` into `<MMC Editor>\Plugins\`.
3. Copy `oodle-data-shared.dll` next to the MMC **editor executable** (not into Plugins).
4. Start MMC → load profile → **Tools → Import Frosty Mod (.fbmod)…** → **File → Save As…** project.

More detail: [docs/mmc-import.md](docs/mmc-import.md).

## After convert (important)

| Path | Required next step |
|------|--------------------|
| **FIFA** | Open project in FET with game loaded → **Save** → export a **new** `.fifamod` → test |
| **MMC** | Plugin import with profile loaded → **Save As** project → export mod → test |

Skipping **Save** is the most common cause of “works in editor, broken in game.”

## Build from source

**Requirements:** [.NET 10 SDK](https://dotnet.microsoft.com/download), .NET Framework 4.8 targeting pack (plugin only), Oodle DLL under `third_party/oodle/bin/`, and for the plugin Frosty/MMC refs in `third_party/mmc-refs/` (see that folder’s README).

```bash
dotnet build FrostyConvert.slnx
dotnet test tests/FrostyConvert.Tests
dotnet run --project src/FrostyConvert.FifaGui
# CLI from source (optional dev host):
dotnet run --project src/FrostyConvert.Cli -- mod.fifamod -o recovered.fifaproject
```

Release zips: `.\scripts\pack-release.ps1 -Version 1.0.0`

## Docs & license

| Doc | Topic |
|-----|--------|
| [docs/fifa-import.md](docs/fifa-import.md) | FIFA recovery workflow |
| [docs/mmc-import.md](docs/mmc-import.md) | MMC live import |
| [docs/formats/](docs/formats/) | Format notes (`.fbmod`, `.fifamod`, projects) |

- Intended for **abandoned** mods and community maintenance — preserve original title / author / version from mod metadata.
- Tool code: **MIT** (see [LICENSE](LICENSE)).
- Format knowledge draws on public Frosty Toolsuite structure ([CadeEvs/FrostyToolsuite](https://github.com/CadeEvs/FrostyToolsuite)). This project reimplements standalone parsers and does not redistribute modified Frosty sources.
- Oodle is from RAD / Epic; use of `oodle-data-shared.dll` follows Epic’s Unreal Engine / OodleUE terms — see `third_party/oodle/README.md`.
