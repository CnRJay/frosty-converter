# FET 1:1 conversion map (`.fifamod` → `.fifaproject`)

Ground truth: decompiled **FIFA Editor Tool** `ModWriter.WriteProject` (FETM) and `EditorProject.Save` / `ReadHeader` (FETP v2).

## What we mirror 1:1

| Source (`.fifamod`) | Destination (`.fifaproject`) |
|---------------------|------------------------------|
| Title, author, categories, version, description, 8 links | Same mod settings header |
| Icon | Icon |
| Screenshots | Screenshots |
| Locale.ini files | Locale.ini files |
| InitFs files | InitFs files |
| PlayerLua free-form map | PlayerLua (new-format load uses version **100** default branch) |
| PlayerKitLua free-form map | PlayerKitLua |
| Added bundles (name, nameHash, superBundleHash) | Added bundles (name, superBundleHash, type=0) |
| EBX index + compressed payload | Directly-modified EBX (IsDirectlyModified forced) |
| EBX BRT fields (hash, path, parent, BundleRefOnly) | Same BRT fields + flags |
| EBX / Res / Chunk added-bundle hashes | Same |
| RES type/RID/meta + payload | Modified Res |
| Chunk GUID, ranges, H32, legacy hash/name, superBundle | Modified Chunk |
| Collectors + BRT name footers | Parsed for inspect; FET rebuilds on export from live state |

## Chunk `IsAdded` rule

`ModWriter` never sets `ChunkFlags.IsAdded`. New legacy files only set `IsLegacyAdded`.  
`EditorProject.Load` requires `IsAdded` to **create** a chunk; without it it looks the GUID up in the TOC and can skip/warn.

FrostyConvert treats a chunk as added when:

- `IsAdded` is set, **or**
- `IsLegacyAdded` is set

## Offline-impossible fields (not 1:1)

| Field | Why |
|-------|-----|
| `AssetSha1AtImport` | Needs live game asset at import time → zeros offline |
| `GamePatchVersionAtImport` | We write the mod’s `gameVersion` (best available) |
| Linked assets graph | Not stored in `.fifamod`; FET rebuilds from live managers after load |
| EBX/Res `IsAdded` when flag clear | Mod list = modified set; TOC membership unknown offline |
| Exact FET tool version bytes | Written as `0.1.0.0` |
| Collectors / BRT name footers on project | Project format has no trailing tables; export regenerates them |
| Password-locked mods (FMT Pro) | Detected via header/decompress heuristics; cannot unlock without author password/key |

## After convert (required for best fidelity)

1. Open `.fifaproject` in FET with the **matching game** loaded  
2. **File → Save** (live types, re-link legacy/collectors)  
3. Export a **new** `.fifamod` for Mod Manager  

That Save step is the same idea as MMC live import + Save As.
