# MMC Editor import (CFB / Madden)

## Why offline `.fbproject` crashes on open-asset

Crash log:

```
Index was out of range. Parameter name: index
  at Frosty.Core.Controls.FrostyPropertyGrid.OnApplyTemplate()
```

College Football / Madden EBX is stored as **RIFF**:

```
RIFF / EBX form
  EBXD  – data
  EFIX  – fixups
  EBXX  – extras
```

MMC’s project pipeline loads EBX with `EbxReader.CreateProjectReader` → `EbxReaderRiff` when `EbxVersion == 6`, using **SharedTypeDescriptors** and live type info from the game.

Offline conversion can:

1. Parse the `.fbmod`
2. Oodle-decompress the CAS blocks
3. Write those RIFF bytes into a project

…but the property grid still blows up because the asset objects were never rehydrated through MMC’s type system the same way a normal Save does (`CreateProjectWriter` / `EbxWriterRiff` from live `EbxAsset` instances).

MMC project format is also **v17** (stock Frosty was v14), with extra load paths.

## Correct recovery path

Use **FrostyConvert.MmcPlugin** while the game is loaded:

| Step | Action |
|------|--------|
| 1 | Build plugin (`dotnet build src/FrostyConvert.MmcPlugin`) |
| 2 | Open MMC, load CFB27 profile |
| 3 | File → Import → Frosty Mod (.fbmod)… |
| 4 | File → Save As → new `.fbproject` |

## Related MMC types (FrostySdk)

- `FrostySdk.IO.EbxReaderRiff`
- `FrostySdk.IO.EbxWriterRiff`
- `FrostySdk.IO.EbxReaderRiffPGA`
- `EbxBaseWriter.CreateProjectWriter` → Riff when version is 6
