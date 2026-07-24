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

That is the main reason **face packs with head variation id ≥ 1** (`…/var_1/…`, `…/var_2/…`) and **created players/teams** looked empty after convert: those paths are always **new** assets (not TOC hits). Created faces use a pure-numeric folder (`…/player_50000/50247/var_0/…`); created kits use a pure-numeric team folder (`…/kit_150000/150039/…`). Pack-only trees (kit numbers, jersey fonts, warpcloth runtimedata, bodyupper, balls, wardrobe, shoe, strand-hair) are also force-added.

Named EA-style **ORIGID** segments (`…/michael_olise_247827/var_0/…`) stay non-added so real-player replacements still resolve via live TOC — their chunk GUIDs already exist; force-adding those chunks makes FET drop payloads (`added chunk … already exists`) and crash texture preview. Exceptions on named faces: **strand-hair** and **face/hair texture maps** (often missing from TOC) are force-added.

FrostyConvert treats assets as added when:

| Kind | Force `IsAdded` when |
|------|----------------------|
| Chunk | Mod flag `IsAdded` / `IsLegacyAdded` only — **never offline-forced**. Project header `GameVersion=0` makes FET treat the project as outdated so missing chunks are `AddChunk()`'d and existing TOC chunks get `ModifiedEntry` applied (force-adding TOC hits orphans payloads). |
| Res / EBX | Mod flag `IsAdded`, **or** path matches `/var_N` (N≥1), created-player/kit numeric folders, pack-only paths (kit numbers, jersey fonts, worlds, warpcloth, balls, wardrobe, shoe, bodyupper), strand-hair, or player texture maps |

For force-added EBX we also write a best-effort type name (path heuristic) and partition GUID from the RIFF `EBXD` block when present.

**Created-face mesh preview:** Named ORIGID heads bind textures via the stock game `MeshVariationDatabase`. Created faces only have a pack MVDB, so FET’s mesh viewer may show unbound materials (rainbow normals). Face `TextureAsset`s open correctly; Blender/export pipelines are unaffected. Offline mesh EBX rewrites are intentionally not applied.

**Linked assets:** recovered projects emit best-effort `HasLinkedAssets` tables (Texture/Mesh EBX → same-name Res → payload chunks; MeshVariationDatabase → sibling face/mesh assets). FET still rebuilds a fuller graph after **File → Save**.

## Offline-impossible fields (not 1:1)

| Field | Why |
|-------|-----|
| `AssetSha1AtImport` | Needs live game asset at import time → zeros offline |
| `GamePatchVersionAtImport` | Written as **0** so assets are outdated and FET reimports after load |
| Project header `GameVersion` | Written as **0** (not the mod’s patch) so `Header.GameVersion != fileSystem.Head` — enables missing-chunk `AddChunk` + AssetReimporter |
| Linked assets graph | Best-effort offline links written; FET still rebuilds fuller graph after File → Save |
| EBX/Res `IsAdded` when flag clear | Forced for created/pack-only paths, texture maps, strand-hair. ORIGID named `var_0` meshes/blueprints stay non-added (TOC mods). Named club kit renames may still need a live FET Save if they miss. |
| Exact FET tool version bytes | Written as `0.1.0.0` |
| Collectors / BRT name footers on project | Project format has no trailing tables; export regenerates them |
| Password-locked mods (FMT Pro) | Detected via header/decompress heuristics; cannot unlock without author password/key |

## After convert (required for best fidelity)

1. Open `.fifaproject` in FET with the **matching game** loaded  
2. **File → Save** (live types, re-link legacy/collectors)  
3. Export a **new** `.fifamod` for Mod Manager  

That Save step is the same idea as MMC live import + Save As.
