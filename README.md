# FrostyConvert

Recover **editable editor projects** from compiled Frostbite mods when the original project is gone.

| Input | Output | How you edit |
|-------|--------|--------------|
| **`.fbmod`** (Frosty / MMC) | Live import into MMC, then **`.fbproject`** | MMC Editor plugin (CFB / Madden) |
| **`.fifamod`** (FIFA Editor Tool) | **`.fifaproject`** | Open in FIFA Editor Tool with the game loaded |

Both paths decompress CAS/Oodle payloads and rebuild projects you can keep maintaining after game updates.

## Download (releases)

Prebuilt Windows packages are attached to each [GitHub Release](../../releases):

| Asset | What it is |
|-------|------------|
| **`FrostyConvert-FifaTool-v*-win-x64.zip`** | Self-contained CLI — convert `.fifamod` → `.fifaproject` (also inspects `.fbmod`) |
| **`FrostyConvert-MmcPlugin-v*.zip`** | MMC Editor plugin — live import of `.fbmod` for CFB/Madden |

### FIFA tool install

1. Download **FifaTool** zip → unzip (only **3 files**: `fbmod2project.exe`, `oodle-data-shared.dll`, `INSTALL.txt`).
2. Run:
   ```bat
   fbmod2project.exe "mod.fifamod" -o recovered.fifaproject
   ```
3. In FIFA Editor Tool: load the game → **File → Open Project** → `recovered.fifaproject`.

Keep the `.exe` and `oodle-data-shared.dll` in the same folder.
### MMC plugin install

1. Download **MmcPlugin** zip → close MMC Editor.
2. Copy everything under `Plugins\` into `<MMC Editor>\Plugins\`.
3. Copy `oodle-data-shared.dll` next to the MMC **editor executable** (not into Plugins).
4. Start MMC → load profile → **Tools → Import Frosty Mod (.fbmod)…** → **File → Save As…** project.

Each zip includes an `INSTALL.txt` with the same steps.

## Requirements

**Building from source:**

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- .NET Framework 4.8 targeting pack (MMC plugin)
- Oodle DLL under `third_party/oodle/bin/` (or downloaded by CI)
- For the plugin: Frosty/MMC reference DLLs in `third_party/mmc-refs/` or `MmcEditorDir` (see `third_party/mmc-refs/README.md`)

## Build from source

```bash
dotnet build FrostyConvert.slnx
dotnet test tests/FrostyConvert.Tests
```

CLI project: `src/FrostyConvert.Cli` (assembly name `fbmod2project`).
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

CFB/Madden gameplay EBX is **RIFF**. Offline `.fbproject` files may open in the asset list but **crash the property grid**. Prefer the **MMC plugin** (from Releases or build from source).

1. Install the plugin (see [Download](#download-releases) above), or build:
   ```bash
   dotnet build src/FrostyConvert.MmcPlugin -p:MmcEditorDir="C:\path\to\MMC_Editor"
   ```
2. Restart **MMC Editor**, load the game profile (e.g. CollegeFB27).
3. **Tools → Import Frosty Mod (.fbmod)…**
4. **File → Save As…** a new `.fbproject`.

Details: [docs/mmc-import.md](docs/mmc-import.md).

## Workflow: EA FC / FIFA (`.fifamod`)

FIFA Editor Tool has **no plugin API**. Use the **FifaTool** release zip (or CLI from source):

```bash
fbmod2project.exe "mod.fifamod" -o recovered.fifaproject
# or from source:
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
scripts/pack-release.ps1      Build both release zips
packaging/                    INSTALL.txt templates for zips
.github/workflows/            CI + tag-triggered releases
docs/                         Format notes and import guides
tests/                        Unit tests (fixtures stay local / gitignored)
third_party/oodle/            Bundled Oodle data DLL
third_party/mmc-refs/         Local Frosty DLLs for plugin compile (gitignored)
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
