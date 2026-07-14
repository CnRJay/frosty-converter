# FIFA Editor Tool recovery (`.fifamod` → `.fifaproject`)

## Why this is not an MMC-style plugin

| | **MMC (CFB/Madden)** | **FIFA Editor Tool v2** |
|--|----------------------|-------------------------|
| Host | Frosty fork with `Plugins/` | Closed **single-file** .NET app (~155 MB) |
| Extension API | `RegisterMenuExtension` | **None** (no `PluginManager`, no Plugins folder) |
| Live import | `FrostyConvert.MmcPlugin` → Tools → Import `.fbmod` | Not possible as a drop-in DLL |
| Project format | `.fbproject` | `.fifaproject` (magic `FETP`) |

FIFA Editor Tool embeds `Modding.dll` / `Sdk.dll` / `FIFAEditorTool.dll` inside the exe. It can **export** `.fifamod` from a project and the Mod Manager can **apply** them, but there is no public menu-extension surface equivalent to MMC.

## Equivalent workflow (same end state as MMC import)

MMC:

1. Load game profile  
2. Tools → Import Frosty Mod (`.fbmod`)  
3. Edit assets  
4. File → Save As `.fbproject`

FIFA Editor Tool (via FrostyConvert):

1. Convert the abandoned mod:
   ```text
   fbmod2project "mod.fifamod" -o recovered.fifaproject
   ```
2. Open **FIFA Editor Tool**, load **FC26** (or matching game)  
3. **File → Open Project** → `recovered.fifaproject`  
4. Edit assets in the property grid  
5. **Save** project / export a new `.fifamod`

Opening the project with the game loaded rehydrates RIFF EBX through FET’s live `EbxReader` + SDK types—the same reason the MMC plugin had to import live instead of only writing offline projects.

## What the converter writes

- Official **FETM** `.fifamod` parse (`Modding.ModReader.ReadNewFormat` layout)
- CAS + Oodle type **0x19** decompress → RIFF EBX
- **FETP** v2 `.fifaproject` with modified EBX stored as **CAS-compressed** blobs (same as the `.fifamod`).  
  FET’s `AssetManager.GetEbx` always runs `Decompression.Decompress` on `ModifiedEntry.Data`, which requires a codec header with **guard bits = 7**. Storing raw RIFF fails with `Invalid guard bits in codec header: expected 7, got 0x0`.

## Limits

- Offline conversion cannot fill `AssetSha1AtImport` from the live game (zeros). FET still loads modified data; out-of-date checks may warn.
- Chunk/res tables are written when present; complex legacy/collector cases may need FET re-save.
- Password-locked mods (FMT salt footer) are not supported yet.

## Install paths (example)

- Editor: `d:\fifa mods\FIFA Editor Tool v2.0.4\`
- Oodle: `oodle-data-shared.dll` next to the CLI (or `--oodle path`)
