# Test fixtures

Local-only sample mods for development and tests.

This directory is **gitignored** except this README and `.gitkeep`, so third-party mods are never published with the repo.

## Suggested files

| File | Used for |
|------|----------|
| `*.fbmod` | CLI inspect / offline `.fbproject` / MMC plugin import |
| `*.fifamod` | CLI inspect / `.fifaproject` conversion |

```text
tests/fixtures/
  example-gameplay.fbmod
  example-gameplay.fifamod
```

## CLI against a local sample

```bash
# Frosty / MMC
dotnet run --project src/FrostyConvert.Cli -- tests/fixtures/example.fbmod --inspect
dotnet run --project src/FrostyConvert.Cli -- tests/fixtures/example.fbmod -o recovered.fbproject

# FIFA Editor Tool
dotnet run --project src/FrostyConvert.Cli -- tests/fixtures/example.fifamod --inspect
dotnet run --project src/FrostyConvert.Cli -- tests/fixtures/example.fifamod -o recovered.fifaproject
```

## Notes

- **CFB / Madden:** offline `.fbproject` is not enough for property-grid editing of RIFF EBX. Prefer the MMC plugin (**Tools → Import Frosty Mod**). See [docs/mmc-import.md](../../docs/mmc-import.md).
- **FIFA / FC:** convert to `.fifaproject`, then open in FIFA Editor Tool with the game loaded. See [docs/fifa-import.md](../../docs/fifa-import.md).
- Unit tests look for `tests/fixtures/*.fifamod` (and related samples) when present; they still pass without them where fixtures are optional.
