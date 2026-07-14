# FIFA Editor Tool recovery (`.fifamod` → `.fifaproject`)

## Workflow (same end state as MMC import)

| MMC | FIFA Editor Tool + FrostyConvert |
|-----|----------------------------------|
| Load game profile | Load game (e.g. FC26) |
| Tools → Import `.fbmod` | CLI: convert `.fifamod` → `.fifaproject` |
| Edit assets | File → Open Project → edit assets |
| File → Save As `.fbproject` | Save project / export a new `.fifamod` |

### Convert

```bash
dotnet run --project src/FrostyConvert.Cli -- "mod.fifamod" -o recovered.fifaproject
```

Optional: `--inspect` first to list resources; `--oodle path` if the Oodle DLL is not next to the CLI.

### Open in the editor

1. Launch **FIFA Editor Tool** and load the matching game.
2. **File → Open Project** → `recovered.fifaproject`.
3. Edit assets in the property grid.
4. Save the project and/or export a new mod.

With the game loaded, FET rehydrates EBX through its live SDK types—the same reason MMC needs live import for RIFF assets.

## What the converter writes

- Official **FETM** `.fifamod` layout (`Modding.ModReader.ReadNewFormat`)
- CAS + Oodle (codec family **0x19** / Leviathan) for payloads
- **FETP** v2 `.fifaproject` with modified EBX stored as **CAS-compressed** blobs (same framing as the mod)

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
