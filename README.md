# FrostyConvert

Recover **editable editor projects** from compiled Frostbite mods when the original project is gone.

| Input | Output | How you edit |
|-------|--------|--------------|
| **`.fbmod`** (Frosty / MMC) | Live import into MMC, then **`.fbproject`** | MMC Editor plugin (CFB / Madden) |
| **`.fifamod`** (FIFA Editor Tool) | **`.fifaproject`** | Open in FIFA Editor Tool with the game loaded |

Both paths decompress CAS/Oodle payloads and rebuild projects you can keep maintaining after game updates.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (CLI and tests)
- .NET Framework 4.8 (MMC plugin only)
- Oodle data DLL (bundled as `third_party/oodle/bin/oodle-data-shared.dll`)

## Build

```bash
dotnet build FrostyConvert.slnx
dotnet test tests/FrostyConvert.Tests
```

CLI entry project: `src/FrostyConvert.Cli` (assembly name `fbmod2project`).

## CLI

```bash
# Inventory a mod (no game install)
dotnet run --project src/FrostyConvert.Cli -- path/to/mod.fbmod --inspect
dotnet run --project src/FrostyConvert.Cli -- path/to/mod.fifamod --inspect

# JSON report + raw payload dump
dotnet run --project src/FrostyConvert.Cli -- mod.fbmod --inspect --json --report report.json --extract ./payloads

# Offline convert
dotnet run --project src/FrostyConvert.Cli -- mod.fbmod -o recovered.fbproject
dotnet run --project src/FrostyConvert.Cli -- mod.fifamod -o recovered.fifaproject

# Optional Oodle override
dotnet run --project src/FrostyConvert.Cli -- mod.fbmod --inspect --oodle path/to/oodle-data-shared.dll
```

| Flag | Meaning |
|------|---------|
| `--inspect` | Parse and print resource inventory |
| `-o` / `--output` | Write `.fbproject` or `.fifaproject` |
| `--json` | JSON report text |
| `--report <path>` | Write JSON report to a file |
| `--extract <dir>` | Dump resource payloads |
| `--oodle <path>` | Override Oodle DLL |
| `--inspect-project` | Summarize a recovered `.fbproject` |

## Workflow: College Football / Madden (`.fbmod`)

CFB/Madden gameplay EBX is **RIFF**. Offline `.fbproject` files may open in the asset list but **crash the property grid**. Prefer live import.

1. Build the plugin:
   ```bash
   dotnet build src/FrostyConvert.MmcPlugin
   ```
   On success it copies into your MMC `Plugins` folder (set `MmcEditorDir` on the project if needed).

2. Restart **MMC Editor**, load the game profile (e.g. CollegeFB27).

3. **Tools → Import Frosty Mod (.fbmod)…**

4. **File → Save As…** a new `.fbproject` so MMC re-serializes assets correctly.

Details: [docs/mmc-import.md](docs/mmc-import.md).

## Workflow: EA FC / FIFA (`.fifamod`)

FIFA Editor Tool is a closed single-file app with **no plugin API**. Convert offline, then open the project in the editor:

```bash
dotnet run --project src/FrostyConvert.Cli -- "mod.fifamod" -o recovered.fifaproject
```

1. Launch **FIFA Editor Tool** and load the matching game (e.g. FC26).
2. **File → Open Project** → `recovered.fifaproject`.
3. Edit assets, then save / re-export a mod.

Details: [docs/fifa-import.md](docs/fifa-import.md).

## Oodle

Frostbite mods commonly use Oodle. This repo uses:

- **`oodle-data-shared.dll`** — Oodle *data* library from [WorkingRobot/OodleUE](https://github.com/WorkingRobot/OodleUE) builds (see `third_party/oodle/`)
- **OozSharp** — managed Kraken fallback for some streams only

You do not need a game `oo2core_*.dll` for typical CFB/Madden or FC26 mods.

## Repository layout

```text
src/FrostyConvert.Core/       Shared parsers and converters
src/FrostyConvert.Cli/       Command-line tool
src/FrostyConvert.MmcPlugin/ MMC Editor import menu
docs/                         Format notes and import guides
tests/                        Unit tests (sample mods stay local / gitignored)
third_party/oodle/            Bundled Oodle data DLL
```

## Documentation

| Doc | Topic |
|-----|--------|
| [docs/formats/fbmod.md](docs/formats/fbmod.md) | Frosty `.fbmod` |
| [docs/formats/fbproject.md](docs/formats/fbproject.md) | Frosty `.fbproject` |
| [docs/formats/fifamod.md](docs/formats/fifamod.md) | FIFA Editor Tool `.fifamod` |
| [docs/mmc-import.md](docs/mmc-import.md) | MMC live import |
| [docs/fifa-import.md](docs/fifa-import.md) | FIFA project recovery |

## Samples

Put real mods under `tests/fixtures/` for local testing. That folder is gitignored so third-party content is not published. See [tests/fixtures/README.md](tests/fixtures/README.md).

## Ethics

- Preserve original **title / author / version** from mod metadata in recovered projects.
- Intended for **abandoned** mods and community maintenance, not for stripping credit from active authors.

## License

Tool code in this repository: **TBD** (set by the project owner before release).

Format knowledge draws on public Frosty Toolsuite structure ([CadeEvs/FrostyToolsuite](https://github.com/CadeEvs/FrostyToolsuite), CC BY-NC-ND 4.0). This project reimplements standalone parsers and does not redistribute modified Frosty sources.

Oodle is a product of RAD Game Tools / Epic. Use of `oodle-data-shared.dll` follows Epic’s Unreal Engine / OodleUE terms — see `third_party/oodle/README.md`.
