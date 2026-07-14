# Test fixtures

Drop real mod samples here for **local** testing. This directory is **gitignored** (except this README) so third-party mods are not published to GitHub.

## Suggested layout

```text
tests/fixtures/
  cnr-gameplaymod.fbmod          # CFB / Madden binary Frosty mod
  some-gameplay.fifamod          # FIFA Editor Tool / FMM mod
```

## Commands

```bash
# Frosty .fbmod
dotnet run --project src/FrostyConvert.Cli -- tests/fixtures/mod.fbmod --inspect
dotnet run --project src/FrostyConvert.Cli -- tests/fixtures/mod.fbmod -o recovered.fbproject

# FIFA .fifamod → open result in FIFA Editor Tool with game loaded
dotnet run --project src/FrostyConvert.Cli -- tests/fixtures/mod.fifamod --inspect
dotnet run --project src/FrostyConvert.Cli -- tests/fixtures/mod.fifamod -o recovered.fifaproject
```

## MMC live import (CFB / Madden)

Offline `.fbproject` is not enough for RIFF EBX property-grid editing. Build the MMC plugin and use **Tools → Import Frosty Mod** instead — see root README.
