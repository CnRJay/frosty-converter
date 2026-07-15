# FIFA Editor Tool recovery (`.fifamod` → `.fifaproject`)

## Workflow (same end state as MMC import)

| MMC | FIFA Editor Tool + FrostyConvert |
|-----|----------------------------------|
| Load game profile | Load game (e.g. FC26) |
| Tools → Import `.fbmod` | CLI: convert `.fifamod` → `.fifaproject` |
| Edit assets | File → Open Project → edit assets |
| File → Save As `.fbproject` | Save project / export a new `.fifamod` |

### Convert

**GUI** (easiest): double-click `FrostyConvert.FifaGui.exe` from the release zip, pick a `.fifamod`, Convert.

**Release zip CLI** (`FrostyConvert-FifaTool-v*-win-x64.zip` from GitHub Releases):

```bat
fbmod2project.exe "mod.fifamod" -o recovered.fifaproject
```

**From source:**

```bash
dotnet run --project src/FrostyConvert.FifaGui
# or CLI:
dotnet run --project src/FrostyConvert.Cli -- "mod.fifamod" -o recovered.fifaproject
```

Optional: `--inspect` first to list resources; `--oodle path` if the Oodle DLL is not next to the CLI.
### Open in the editor

1. Launch **FIFA Editor Tool** and load the matching game.
2. **File → Open Project** → `recovered.fifaproject`.
3. Edit assets in the property grid.
4. Save the project and/or export a new mod.

With the game loaded, FET rehydrates EBX through its live SDK types—the same reason MMC needs live import for RIFF assets.

### Where assets appear (important for texture/UI mods)

| Mod content | Where to look in FET |
|-------------|----------------------|
| Gameplay / EBX | Main Data Explorer → **Search EBX (non-legacy)** + Show Only Modified |
| RES (textures, etc.) | **Chunk/Res** explorer + Show Only Modified |
| Legacy UI (`.big`, fonts, `data/ui/…`) | **Legacy** explorer + Show Only Modified |
| **Promoted crest/UI `.dds`** | Data Explorer under `content/ui/legacy/…` (see below) |

### Promote legacy assets → Data Explorer

World Cup–style packs mix **legacy DDS** (crests/UI images) with **`.big` Apt UI**, fonts, and a few `content/` Texture RES. FET **Data Explorer only lists EBX**, so:

```bat
fbmod2project.exe "WorldCup.fifamod" -o recovered.fifaproject --promote-legacy-textures
```

| Source in `.fifamod` | After promote | Where to edit in FET |
|----------------------|---------------|----------------------|
| Legacy `.dds` (all paths) | `TextureAsset` under `content/ui/legacy/…` | **Data Explorer** |
| Existing `content/…` Texture RES | TextureAsset EBX wrapper (same name) | **Data Explorer** |
| Legacy `.big` (Scaleform/Apt UI) | unchanged | **Legacy Explorer** → Big File editor |
| `.ttf` / `.xml` / `.txt` | unchanged | **Legacy Explorer** |

Optional flags:

| Flag | Default | Meaning |
|------|---------|---------|
| `--texture-prefix` | `content/ui/legacy` | Name root for promoted DDS |
| `--texture-filter` | *(empty = all .dds)* | Substring filter on legacy paths |
| `--texture-max` | unlimited | Cap for testing |
| `--extract-legacy dir` | — | Dump raw legacy files to disk |

Original legacy entries are always kept for Mod Manager export. After editing TextureAssets, **File → Save** in FET before export.

```bat
fbmod2project.exe "mod.fifamod" -o recovered.fifaproject --extract-legacy out\legacy
```

## What the converter writes

- Official **FETM** `.fifamod` layout (`Modding.ModReader.ReadNewFormat`)
- CAS + Oodle (codec family **0x19** / Leviathan) for payloads
- **FETP** v2 `.fifaproject` with **EBX, Res, and Chunks** (texture/mesh mods that are chunk-heavy are supported)
- Payloads stored as the mod’s compressed blobs (FET decompresses via codec headers)

FET’s `AssetManager.GetEbx` always runs `Decompression.Decompress` on `ModifiedEntry.Data`. That path requires a codec header with **guard bits = 7**. Storing raw RIFF fails with:

```text
Invalid guard bits in codec header: expected 7, got 0x0
```

## Limits

- Offline conversion cannot fill `AssetSha1AtImport` from the live game (zeros are written). FET still loads modified data; out-of-date checks may warn.
- Chunk/res entries are written when present; complex legacy/collector cases may need a re-save inside FET.
- Password-locked mods are not supported yet.

## Related docs

- Format notes: [formats/fifamod.md](formats/fifamod.md)
- Root overview: [../README.md](../README.md)
