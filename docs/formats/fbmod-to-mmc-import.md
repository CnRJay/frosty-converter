# MMC plugin 1:1 import map (`.fbmod` → live editor)

Ground truth: MMC Editor **1.1.0.1**

- `Frosty.Core.IO.FrostyModReader` / `FrostyModCryptor`
- `Frosty.Core.Mod.BaseModResource` + `Ebx` / `Res` / `Chunk` / `Bundle` / `FsFile` resources
- `Frosty.ModSupport.FrostyModExecutor.ProcessModResources` (apply semantics)
- `Frosty.Core.Mod.IModCustomActionHandler` (`Load` + `Modify`)
- `Frosty.Core.Handlers.LegacyCustomActionHandler`
- `FrostySdk.Managers.AssetManager` (`Add*` / `Modify*`)

Plugin version: **1.0.8+**

## Parse (= `FrostyModReader` + decrypt)

| Item | Status |
|------|--------|
| Magic, version 1–8, profile, gameVersion | Yes |
| Details + link (v≥5) | Yes |
| Resource table (all types 0–5) | Yes |
| Chunk h64 / superBundles (MMC CFB/Madden) | Yes |
| Bundle name + superBundle FNV | Yes |
| FsFile base fields | Yes |
| Data section AES+HMAC v8 (`FMENC001`) | Yes |
| Per-resource payload load | Yes |

## Apply (= editor equivalent of `ProcessModResources`)

| Resource | Action |
|----------|--------|
| **Bundle** | `AddBundle(name, None, sbIndex)`; FNV superBundle → index |
| **Ebx** (data) | CAS decompress → RIFF/factory `EbxReader` → `ModifyEbx` **or** `AddEbx(name, asset, bundles)` if missing |
| **Ebx** (handler) | `GetCustomHandler(uint)` → `Load` + `Modify` → `HandlerExtraData` + `ModifiedEntry.Data` |
| **Res** (data) | CAS decompress → `ModifyRes` / `AddRes` if missing |
| **Res** (handler) | `GetCustomHandler(ResourceType)` → `Load` + `Modify` (same as executor) |
| **Chunk** (data) | CAS decompress → `ModifyChunk` / `AddChunk` (force-add if TOC miss) |
| **Chunk** (legacy `0xBD9BFB65`) | `LegacyCustomActionHandler.Load` → update `LegacyFileEntry` collectors **and** `Modify` → collector chunk `ModifiedEntry.Data` |
| **Chunk** (other handler) | `GetCustomHandler(uint)` → `Load` + `Modify` |
| **Runtime resources** from `Modify` | Re-apply ebx/res/chunk payloads emitted into `RuntimeResources` |
| **FsFile** | Parse `DbObject`; invoke `FileSystemManager.WriteInitFs` when available; else custom-asset fallback |
| **Meta** | `AddedBundles` (FNV→id→`AddToBundle`), `IsInline`, `UserData`, chunk geometry/H64/superBundles/`IsTocChunk` |
| **Texture UX** | Link same-name Res→Ebx for Show Modified |

## Order

1. Bundles  
2. Non-handler chunks  
3. Res  
4. Ebx  
5. Handler chunks  
6. FsFile  

## After import

**File → Save As…** a new `.fbproject` so MMC rewrites with live types.

## Note on Mod Manager launch

CAS packing / `WriteInitFs` to a *patch* tree is the **Mod Manager launch** path. The plugin targets **editor** 1:1 (live `AssetManager` + project Save). FsFile that only exists in launch patching is still imported into the FS when the live API exists.
