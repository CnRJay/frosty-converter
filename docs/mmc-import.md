# MMC Editor import (CFB / Madden)

## Why offline `.fbproject` is not enough

Crash when opening an asset in the property grid:

```text
Index was out of range. Parameter name: index
  at Frosty.Core.Controls.FrostyPropertyGrid.OnApplyTemplate()
```

College Football / Madden EBX is stored as **RIFF**:

```text
RIFF / EBX form
  EBXD  – data
  EFIX  – fixups
  EBXX  – extras
```

MMC loads project EBX with `EbxReader.CreateProjectReader` → `EbxReaderRiff` when `EbxVersion == 6`, using **SharedTypeDescriptors** and live type info from the game.

Offline conversion can:

1. Parse the `.fbmod`
2. Oodle-decompress CAS blocks
3. Write RIFF bytes into a `.fbproject`

…but the property grid still fails because objects were never rehydrated through MMC’s type system the way a normal **Save** does (`CreateProjectWriter` / `EbxWriterRiff` from live `EbxAsset` instances).

MMC’s project format is also **v17** (stock Frosty was often v14), with extra load paths.

## Correct recovery path

### From a GitHub Release (recommended)

1. Download **`FrostyConvert-MmcPlugin-v*.zip`** from Releases.
2. Close MMC. Copy `Plugins\FrostyConvert.MmcPlugin.dll` → `<MMC Editor>\Plugins\`.
3. Copy `oodle-data-shared.dll` next to the MMC editor **exe**.
4. Start MMC → load profile → **Tools → Import Frosty Mod (.fbmod)…** → **File → Save As…**

See `INSTALL.txt` in the zip.

### From source

| Step | Action |
|------|--------|
| 1 | Put Frosty DLLs in `third_party/mmc-refs/` or pass `-p:MmcEditorDir=...` |
| 2 | `dotnet build src/FrostyConvert.MmcPlugin` (auto-deploys if `MmcEditorDir\Plugins` exists) |
| 3 | Restart MMC (plugin DLLs lock while the editor runs) |
| 4 | Load CFB27 / Madden (or matching) profile |
| 5 | **Tools → Import Frosty Mod (.fbmod)…** |
| 6 | **File → Save As…** a new `.fbproject` |

```bash
dotnet build src/FrostyConvert.MmcPlugin -p:MmcEditorDir="C:\path\to\MMC_Editor"
```

Or pack a release-style zip locally: `.\scripts\pack-release.ps1`
## What the plugin does (1:1 with MMC 1.1.0.1 apply)

Full field map: [formats/fbmod-to-mmc-import.md](formats/fbmod-to-mmc-import.md).

1. Parses binary `.fbmod` **v1–v8** (encrypted `FMENC001`, h64/superBundles, all resource types including FsFile)
2. Applies via live `AssetManager` in order: **bundles → chunks → res → ebx → handlers → FsFile**
3. **Added / missing** assets: `AddEbx(name, EbxAsset)`, `AddRes`, `AddChunk` (force-add on TOC miss)
4. **Handlers**: full `IModCustomActionHandler.Load` + `Modify` + `HandlerExtraData` + `ModifiedEntry.Data` (same as `ProcessModResources`)
5. **Legacy collectors** (`0xBD9BFB65`): update `LegacyFileEntry` **and** rewrite collector chunk via `Modify`
6. **FsFile**: `DbObject` parse + `WriteInitFs` / custom-asset when live APIs exist
7. Bundle membership (FNV), geometry, superBundles, inline/TOC flags, userData
8. Texture Res→Ebx linking for Show Modified
9. **File → Save As…** for a native `.fbproject`

### Texture-only mods (e.g. coach portraits)

Many cosmetic mods ship **281 Res + 281 chunks** and **0 EBX**. Import still succeeds; after linking you should see hundreds of TextureAssets under Modified. Always **File → Save As…** a new `.fbproject` after import.

## Related MMC / FrostySdk types

- `FrostySdk.IO.EbxReaderRiff`
- `FrostySdk.IO.EbxWriterRiff`
- `FrostySdk.IO.EbxReaderRiffPGA`
- `EbxBaseWriter.CreateProjectWriter` → Riff when EBX version is 6
