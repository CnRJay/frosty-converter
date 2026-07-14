# FrostyConvert

Recover editable Frosty **`.fbproject`** files from compiled **`.fbmod`** mods.

When a mod author disappears and only the shipped `.fbmod` remains, this tool aims to rebuild a project you can open in Frosty Editor (or compatible forks) and continue development after game patches.

> **Status:** Working for **MMC live `.fbmod` import** (CFB/Madden) and **`.fifamod` → `.fifaproject`** (FIFA Editor Tool).

## Why this exists

- `.fbproject` = editor project (full editable state)
- `.fbmod` = compiled mod for Frosty Mod Manager (lossy export)

Export is not perfectly reversible (e.g. transient-only ebx edits are dropped), but enough data remains to reconstruct a **workable** project for maintenance.

## Quick start

```bash
dotnet build FrostyConvert.slnx
dotnet run --project src/FrostyConvert.Cli -- path/to/mod.fbmod --inspect
```

### Inspect (no game install)

```bash
# Human-readable inventory
dotnet run --project src/FrostyConvert.Cli -- mod.fbmod --inspect

# JSON report
dotnet run --project src/FrostyConvert.Cli -- mod.fbmod --inspect --json --report report.json

# Dump raw resource payloads
dotnet run --project src/FrostyConvert.Cli -- mod.fbmod --inspect --extract ./out-payloads
```

### Convert offline (no game install)

```bash
dotnet run --project src/FrostyConvert.Cli -- mod.fbmod -o recovered.fbproject
```

**What you get:** a `.fbproject` with mod metadata + decompressed assets. Useful for inventory / salvage.

**CFB / Madden MMC limitation:** gameplay EBX is **RIFF** (`EBXD`/`EFIX`/`EBXX`). Offline projects can **open** but **crash the property grid** when you click an asset (`Index was out of range`). Do **not** use offline `.fbproject` for editing those titles.

### Recover for editing (MMC Editor plugin) — recommended for CFB27

```bash
dotnet build src/FrostyConvert.MmcPlugin/FrostyConvert.MmcPlugin.csproj
```

This deploys `FrostyConvert.MmcPlugin.dll` into your MMC `Plugins` folder (default path in the csproj: `d:\cfb27 mods\MMC_Editor_v1.1.0.0`).

Then in **MMC Editor**:

1. **Close and restart MMC** after building (plugin DLLs are locked while the editor runs)  
2. Load the **CollegeFB27** (or matching) game profile  
3. **Tools → Import Frosty Mod (.fbmod)…**  
4. Select the abandoned `.fbmod`  
5. **File → Save As…** a new `.fbproject` (MMC re-serializes EBX correctly)

The plugin applies assets through the live `AssetManager` + `EbxReaderRiff`, so the property grid works.

### Oodle

`oo2core_*.dll` is **proprietary** (RAD/Epic). What we ship natively is:

- **`oodle-data-shared.dll`** — Unreal Engine Oodle *data* library build from [WorkingRobot/OodleUE](https://github.com/WorkingRobot/OodleUE) (see `third_party/oodle/`)
- **OozSharp** — pure managed Kraken reimplementation as a limited fallback

You do **not** need to hunt for `oo2core` for typical CFB/Madden mods.

### Convert with game path (planned)

```text
fbmod2project mod.fbmod -g "C:\Games\..." -o recovered.fbproject
```

Will resolve bundle names and improve handler fidelity via Frosty SDK.

## Repo layout

```text
src/FrostyConvert.Core/      # Format parsers + converters
src/FrostyConvert.Cli/      # fbmod2project CLI
src/FrostyConvert.MmcPlugin # MMC Editor import menu
docs/                        # Format notes + import guides
tests/                       # Unit tests (fixtures stay local / gitignored)
third_party/oodle/           # oodle-data-shared.dll (see third_party/oodle/README.md)
FrostyToolsuite/             # Optional local reference clone (not required to build)
```

## Roadmap

| Phase | Deliverable |
|-------|-------------|
| **1** | Binary `.fbmod` inspect + offline `.fbproject` + MMC live import plugin |
| **2** | `.fifamod` inspect + `.fifaproject` recovery for FIFA Editor Tool |
| Later | Game-assisted bundle resolve, legacy `.archive`, handlers |

### Recover a `.fifamod` for FIFA Editor Tool

FET is a closed single-file app (no `Plugins/` folder), so import cannot mirror the MMC DLL. Convert then open:

```text
fbmod2project "mod.fifamod" --inspect
fbmod2project "mod.fifamod" -o recovered.fifaproject
```

Then in FIFA Editor Tool: load FC26 → File → Open Project → `recovered.fifaproject`.  
Details: [docs/fifa-import.md](docs/fifa-import.md).

## Samples

Place real abandoned mods under `tests/fixtures/` (gitignored). See `tests/fixtures/README.md`.

## Format reference

- [docs/formats/fbmod.md](docs/formats/fbmod.md)
- [docs/formats/fbproject.md](docs/formats/fbproject.md)
- [docs/formats/fifamod.md](docs/formats/fifamod.md)

Upstream reference source: [CadeEvs/FrostyToolsuite](https://github.com/CadeEvs/FrostyToolsuite) (vendored under `FrostyToolsuite/` for research).

## Ethics

- Preserve original **author / title / version** from mod metadata in recovered projects.
- Intended for **abandoned** mods and community maintenance, not for stripping credit from active authors.

## License

Tool code in this repository: TBD (to be set by the project owner).

Frosty Toolsuite itself is licensed under **CC BY-NC-ND 4.0** — do not redistribute modified Frosty sources as a derivative product. This project reimplements format parsers from public structure and will call into stock Frosty assemblies for full conversion where needed.
