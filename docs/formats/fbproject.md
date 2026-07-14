# `.fbproject` format (Frosty Toolsuite 1.0.6.x)

Reference: `Frosty.Core.FrostyProject` in FrostyPlugin / Frosty Toolsuite.

FrostyConvert writes stock-style **v14** projects offline. MMC forks may use **v17** and should be produced via **live import + Save** when editing RIFF EBX (CFB/Madden). See [../mmc-import.md](../mmc-import.md).

## Header

| Field | Type | Notes |
|-------|------|-------|
| magic | u64 | `0x00005954534F5246` (`FROSTY` + `0x0000`) |
| version | u32 | Stock Frosty: **14**; MMC observed up to **17** |
| gameProfile | null-terminated string | Must match loaded profile |
| creationDate | i64 | `DateTime.Ticks` |
| modifiedDate | i64 | `DateTime.Ticks` |
| gameVersion | u32 | `FileSystem.Head` at save |

Versions **&lt; 9** used a different (DbObject) layout and are rejected by modern `InternalLoad`.

## Mod settings

Null-terminated strings:

1. Title, Author, Category, Version, Description  

Then:

| Field | Type |
|-------|------|
| icon length | i32 |
| icon bytes | optional |
| screenshot[4] | each: length i32 + bytes |

## Added assets

Counts are written by seeking back over a `0xDEADBEEF` placeholder (pattern used throughout).

1. **Superbundles** — count (currently always 0 in stock Frosty)
2. **Bundles** — name, superbundle name, type (`BundleType` as i32)
3. **Ebx** — name, Guid  
4. **Res** — name, resRid, resType, resMeta (16 bytes)  
5. **Chunks** — Guid id, h32  

## Modified assets

### Ebx (v13+)

For each:

- name  
- linked assets (see below)  
- added bundle **names** (count + null-terminated names)  
- `hasModifiedData` bool  
- if modified: `isTransient`, `userData`, `isCustomHandler` bool, length, payload  

Custom handler payloads are `ModifiedResource.Save()` blobs. Otherwise uncompressed **project** ebx.

### Res (v13+)

- name, linked assets, added bundles  
- if modified: sha1, originalSize, resMeta, userData, length, payload  
- `sha1 == Zero` means payload is `ModifiedResource`  

### Chunks (v14)

- id (Guid)  
- added bundles  
- firstMip, h32 (even if not modified — v14)  
- `hasModifiedData`  
- if modified: sha1, logicalOffset/Size, rangeStart/End, addToChunkBundle, userData, data  

### Custom actions

Count (stock writes `1`), then legacy handler section (`"legacy"` + entries).

## Linked assets

```
count: i32
for each:
  type: null-terminated ("ebx" | "res" | "chunk" | custom)
  if chunk: Guid
  else: null-terminated name
```

## Implications for fbmod → fbproject

| Mod has | Project needs |
|---------|----------------|
| Compressed game ebx | Decompress → parse → store project ebx or ModifiedResource |
| Bundle FNV hashes | Resolve to **names** via AssetManager |
| No explicit links | Reconstruct from asset graph where possible |
| Handler merge deltas | Store as `DataObject = ModifiedResource`, not raw game bytes |
| Icon/screenshots | Copy into mod settings |

Game install + profile plugins are required for a loadable project. Inspect-only mode cannot produce `.fbproject`.
