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

## Chunk / EBX / Res `IsAdded` rule

`ModWriter` **never** writes `IsAdded` for EBX or Res (it clears those bits). New legacy files only set `IsLegacyAdded` on chunks.  
`EditorProject.Load` requires project `IsAdded` to **create** an asset; without it FET looks the name/GUID up in the live TOC and, on a miss, logs *"doesn't exist"* and leaves an orphan entry that **never appears in Data Explorer**.

That is the main reason **face packs with head variation id ≥ 1** (`…/var_1/…`, `…/var_2/…`) looked empty after convert: those paths are always **new** assets (duplicated from `var_0` in FET), not TOC hits.

FrostyConvert treats assets as added when:

| Kind | Force `IsAdded` when |
|------|----------------------|
| Chunk | Mod flag `IsAdded`, **or** `IsLegacyAdded`, **or** chunk GUID is referenced only by force-added Res (e.g. texture/mesh of a `var_N` N≥1 face) |
| Res / EBX | Mod flag `IsAdded`, **or** asset path matches `/var_N` with **N ≥ 1** (including `var_1_starhead_brt`) |

For force-added EBX we also write a best-effort type name (path heuristic) and partition GUID from the RIFF `EBXD` block when present.

## Offline-impossible fields (not 1:1)

| Field | Why |
|-------|-----|
| `AssetSha1AtImport` | Needs live game asset at import time → zeros offline |
| `GamePatchVersionAtImport` | We write the mod’s `gameVersion` (best available) |
| Linked assets graph | Not stored in `.fifamod`; FET rebuilds from live managers after load |
| EBX/Res `IsAdded` when flag clear | Mod never stores it; we force it for `var_N` (N≥1) head variations and their exclusive chunks. Other truly-new `var_0` assets still need a live FET Save to re-link if TOC miss occurs. |
| Exact FET tool version bytes | Written as `0.1.0.0` |
| Collectors / BRT name footers on project | Project format has no trailing tables; export regenerates them |
| Password-locked mods (FMT Pro) | Detected via header/decompress heuristics; cannot unlock without author password/key |

## After convert (required for best fidelity)

1. Open `.fifaproject` in FET with the **matching game** loaded  
2. **File → Save** (live types, re-link legacy/collectors)  
3. Export a **new** `.fifamod` for Mod Manager  

That Save step is the same idea as MMC live import + Save As.
