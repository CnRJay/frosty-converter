# `.fifamod` format (FIFA Editor Tool / FIFA Mod Manager)

Reference: official `Modding.ModReader.ReadNewFormat` (extracted from FIFA Editor Tool v2.0.4) and FC26 samples.

Magic: little-endian u32 **`FETM`** (`0x4D544546`).

## Header (new format)

| Field | Type |
|-------|------|
| magic | u32 LE `FETM` |
| modVersion | u8 |
| gameName | length-prefixed string (7-bit len + UTF-8) |
| gameVersion | u24 LE |
| title, author | length-prefixed |
| mainCategory | u8 |
| subCategory | u8 |
| customCategory, secondCustomCategory | length-prefixed |
| version, description | length-prefixed |
| links (8) | length-prefixed each |
| icon | 7-bit len + bytes |
| screenshots | count + each (7-bit len + bytes) |
| locale.ini entries | count + (desc, contents) strings |
| initfs files | count + (name, data) |
| player lua / kit lua | nested string tables |
| **dataBaseOffset** | u32 LE — base for all payload offsets |
| added bundles | u24 count + (name, u64 hash, u32 superBundle) |

## EBX index

u24 count, then each:

| Field | Type |
|-------|------|
| name | length-prefixed |
| flags | u8 (`FifamodEbxFlags`) |
| sha1 | 20 bytes (of **compressed** payload) |
| relativeOffset | 7-bit long |
| length | 7-bit int (compressed size) |
| originalSize | 7-bit int |
| optional BRT / added bundles | depending on flags |

Absolute payload offset = `dataBaseOffset + relativeOffset`.

## Payload

CAS-framed Oodle (type **0x19**, often BE header `1970`):

| Field | Type |
|-------|------|
| uncompressedSize | u32 **BE** |
| type | u16 **BE** `0x1970` |
| compressedSize | u16 **BE** |
| payload | OodleLZ |

Gameplay EBX decompresses to **RIFF** (`EBXD` / `EFIX`).

## RES / Chunk

Similar index tables with type-specific fields (type, RID/meta for res; GUID + flags for chunks).

## Recovery tooling

FIFA Editor Tool has no plugin API. Convert offline, then open the project with the game loaded:

```bash
dotnet run --project src/FrostyConvert.Cli -- "mod.fifamod" -o recovered.fifaproject
```

See [../fifa-import.md](../fifa-import.md) for the full workflow and CAS payload notes.