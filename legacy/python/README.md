# FluxerTools (Python, legacy)

Python port using [fluxer.py](https://pypi.org/project/fluxer.py/) with **`!` prefix commands** (no slash commands).

## Quick start

**Windows:** `setup.bat` then `run.bat`

**Docker / NAS:**

```bash
cp config.env.example config.env
docker compose --env-file config.env up -d --build
```

## Commands

| Command | Description |
|---------|-------------|
| `!image <prompt>` | DALL·E 3 image |
| (type in TTS channel while in VC) | Auto-TTS |
| `!ss help` | Secret Santa help |
| `!ss start` / `shuffle` / `stop` | Event lifecycle (mod) |
| `!ss wishlist` / `giftee` / `ask_giftee` | Participant commands |

See also `SECRET_SANTA_COMMANDS.md`, `FLUXER_CAPABILITIES.md`, `TROUBLESHOOTING.md`.

**Note:** The C# bot at repo root is the maintained version with slash commands and `/distribute`.
