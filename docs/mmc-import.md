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

Use **FrostyConvert.MmcPlugin** with the game profile loaded:

| Step | Action |
|------|--------|
| 1 | `dotnet build src/FrostyConvert.MmcPlugin` |
| 2 | Restart MMC (plugin DLLs lock while the editor runs) |
| 3 | Load CFB27 / Madden (or matching) profile |
| 4 | **Tools → Import Frosty Mod (.fbmod)…** |
| 5 | **File → Save As…** a new `.fbproject` |

The build deploys the plugin into MMC’s `Plugins` folder. Override the install root with MSBuild if needed:

```bash
dotnet build src/FrostyConvert.MmcPlugin -p:MmcEditorDir="C:\path\to\MMC_Editor"
```

Also ensure `oodle-data-shared.dll` sits next to the MMC editor exe (the build copies it when available).

## What the plugin does

1. Parses binary `.fbmod` (v1–v7 resource table)
2. Decompresses CAS / Oodle payloads
3. Applies assets via live `AssetManager` (`ModifyEbx` / res / chunk) using `EbxReaderRiff` / factory readers
4. Leaves you free to edit and save a native MMC project

## Related MMC / FrostySdk types

- `FrostySdk.IO.EbxReaderRiff`
- `FrostySdk.IO.EbxWriterRiff`
- `FrostySdk.IO.EbxReaderRiffPGA`
- `EbxBaseWriter.CreateProjectWriter` → Riff when EBX version is 6
