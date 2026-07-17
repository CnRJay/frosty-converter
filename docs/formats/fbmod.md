# `.fbmod` format (Frosty Toolsuite 1.0.6.x)

Reference: [CadeEvs/FrostyToolsuite](https://github.com/CadeEvs/FrostyToolsuite) (e.g. `1.0.6.3`).

## Variants

| Kind | Detection | Payload storage |
|------|-----------|-----------------|
| **Binary** (v1–v7) | Magic `0x01005954534F5246` (`FROSTY` + `0x0001`) | Inline data section after resource table |
| **Legacy** | No binary magic; DbObject header | External `name_NN.archive` sidecars |

FrostyConvert fully inspects and converts **binary** mods. Legacy is detected and reported; full legacy import is not implemented yet.

### Tooling

```bash
dotnet run --project src/FrostyConvert.Cli -- mod.fbmod --inspect
dotnet run --project src/FrostyConvert.Cli -- mod.fbmod -o recovered.fbproject
```

For CFB/Madden editing, prefer live import: [../mmc-import.md](../mmc-import.md).

## Binary header

Little-endian unless noted.

| Field | Type | Notes |
|-------|------|-------|
| magic | u64 | `0x01005954534F5246` |
| version | u32 | Open Frosty documents **1–5**; Madden/CFB forks **6–7**; MMC **1.1.0.1+** writes **8** (encrypted payloads) |
| dataOffset | i64 | Absolute file offset of data manifest table |
| dataCount | i32 | Number of payload entries in the manifest |
| profileName | length-prefixed string | `BinaryWriter.Write(string)` (7-bit length + bytes) |
| gameVersion | i32 | Game head / layout version at export time |

### Version history (from Frosty source comments)

1. Start of binary format; custom data handlers (legacy files)
2. Merging of defined res files (e.g. ShaderBlockDepot)
3. User data on resources
4. Structural changes; removal of modifiedBundles (only *added* bundles stored)
5. Mod page link field
6–7. Used by private Madden/College Football Frosty forks (not in CadeEvs 1.0.6.3).  
   Version **&gt; 5** adds a superBundle list on **Chunk** resources. Version **≥ 7** on CollegeFB27/Madden27 also inserts an **h64** (i64) after **h32** when the chunk has no custom handler. Header/details layout is otherwise the same as v5.
8. MMC Editor / Mod Manager **1.1.0.1+**: same resource table as v7, but every data-section blob is **AES-256-CBC + HMAC-SHA256** protected (see [Encrypted payloads](#encrypted-payloads-v8--mmc-1101)). Older managers reject version 8.

## Mod details

Null-terminated ASCII/UTF-8 strings (byte `0` terminator):

1. Title  
2. Author  
3. Category  
4. Version  
5. Description  
6. Link — **only if version ≥ 5**

## Resource table

```
count: i32
for each resource:
  type: u8          // 0 Embedded, 1 Ebx, 2 Res, 3 Chunk, 4 Bundle
  resourceIndex: i32  // index into data manifest, or -1 if no payload
  name: null-terminated string  // rules depend on version (see below)
  if resourceIndex != -1:
    sha1: 20 bytes
    size: i64         // original / logical size
    flags: u8
    handlerHash: i32
    userData: null-terminated string  // if version >= 3
  bundles: ...        // version-dependent
  type-specific fields
```

### Name field

- **version ≤ 3:** name present only when `resourceIndex != -1`
- **version > 3:** name always present (base class), even for empty embeds

### Flags (`flags` byte)

| Bit | Meaning |
|-----|---------|
| `0x01` | Inline |
| `0x02` | TOC chunk / special chunk-bundle behavior |
| `0x04` | Manifest-layout related (chunk without modified data path) |
| `0x08` | **Added** asset (not an edit of an existing one) |

### Bundles

- **version ≤ 3** and has payload: list of *existing* bundle hashes (ignore on import) then list of *added* bundle hashes  
- **version > 3:** only **added** bundle hashes (`count` + `i32` FNV hashes)

Bundle hashes are FNV1 of lowercased bundle name, with a special case for 8-char hex names.

### Type-specific

**Res**

| Field | Type |
|-------|------|
| resType | u32 |
| resRid | u64 |
| resMeta length | i32 |
| resMeta | bytes |

**Chunk**

| Field | Type | Notes |
|-------|------|-------|
| rangeStart | u32 | |
| rangeEnd | u32 | |
| logicalOffset | u32 | |
| logicalSize | u32 | |
| h32 | i32 | |
| h64 | i64 | **MMC only:** version ≥ 7, `handlerHash == 0`, and profile is CollegeFB27 / Madden27 |
| firstMip | i32 | |
| superBundles count + ints | i32 + i32×N | **MMC only:** version &gt; 5 (count may be 0) |

Open Frosty 1.0.6.x stops at `firstMip` (no `h64` / superBundles). MMC CollegeFB/Madden forks extend the layout as above; FrostyConvert matches that so large texture `.fbmod`s parse without `EndOfStreamException`.

**Bundle**

After base fields, Bundle resource re-reads:

| Field | Type |
|-------|------|
| name | null-terminated (bundle name) |
| superBundleName | i32 (FNV1a hash of superbundle name in writer) |

**Embedded / Ebx**

No extra fields after bundles.

### Custom handlers

Non-zero `handlerHash` means the payload is **not** a full asset stream. It is handler-defined (often a `ModifiedResource` merge delta). Special case:

- `0xBD9BFB65` — Legacy file collector (`LegacyCustomActionHandler`)

## Data section

At `dataOffset`:

```
for i in 0 .. dataCount-1:
  offset: i64   // relative to start of payload region
  size:   i64   // byte length of the blob as stored (encrypted length for v8)
payload region starts at: dataOffset + dataCount * 16
payload for index i: payloadBase + offset, length size
```

Embedded resources 0–4 are typically Icon + Screenshot0..3.

### Encrypted payloads (v8 / MMC 1.1.0.1+)

When the blob begins with ASCII `FMENC001`, the stored payload is:

```
magic:      8 bytes  "FMENC001"
iv:        16 bytes  random AES IV
mac:       32 bytes  HMAC-SHA256(magic ‖ iv ‖ ciphertext)
ciphertext: N bytes  AES-256-CBC PKCS7 of the original CAS/compressed bytes
```

Resource-table SHA1 and logical `size` still describe the **plaintext** compressed asset. FrostyConvert decrypts automatically in `FbmodReader` / the MMC import plugin. Keys are derived from fixed material in the MMC client (shared, not per-author).

## EBX payload note

Exported ebx data is written with the **game** ebx writer and then **compressed** (`Utils.CompressFile`). Project files store **uncompressed** project-format ebx (`EbxBaseWriter.CreateProjectWriter` with transient fields). Conversion must decompress → `EbxReader` → re-store in project form.

## Related types in Frosty

- `Frosty.Core.Mod.FrostyMod`
- `Frosty.Core.IO.FrostyModReader` / `FrostyModWriter`
- `Frosty.Core.Mod.BaseModResource` and subclasses
