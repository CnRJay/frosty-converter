# MMC / Frosty reference assemblies (compile-only)

The MMC plugin links against APIs from your **MMC Editor** install. Those DLLs are **not** redistributed by FrostyConvert.

## Setup (developers / CI)

Copy these three files from your MMC Editor folder (same directory as `MMCEditor.exe` / `FrostyEditor.exe`):

```text
FrostyCore.dll
FrostySdk.dll
FrostyControls.dll
```

into this directory:

```text
third_party/mmc-refs/
  FrostyCore.dll
  FrostySdk.dll
  FrostyControls.dll
  README.md          (this file)
```

Or point the build at your install:

```bash
dotnet build src/FrostyConvert.MmcPlugin -p:MmcEditorDir="C:\path\to\MMC_Editor"
```

## Release packages

The published **MMC plugin zip** does **not** include these Frosty DLLs. End users already have them inside MMC. They only copy FrostyConvert’s plugin files into `Plugins\` (and Oodle next to the editor exe). See the root README.
