# WaveTech Fluxer Toolbox

Fluxer port of [WaveTechToolBoxx](https://github.com/trolle6/WaveTechToolBoxx) — TTS, DALL·E, Secret Santa, and file distribution.

**GitHub:** [github.com/trolle6/Fluxer-TTS](https://github.com/trolle6/Fluxer-TTS)

**Primary implementation:** C# / .NET 8 (`WaveTechFluxerTTS.sln`) — slash commands, full feature set including `/distribute`.

**Legacy:** Python + `fluxer.py` with `!` prefix commands lives in [`legacy/python/`](legacy/python/) on GitHub (Docker deploy still works from that folder).

**Leaving Discord?** See [LEAVE_DISCORD.md](LEAVE_DISCORD.md).

## Features

| Module | Commands / behavior |
|--------|---------------------|
| **TTS** | Auto-TTS in voice; `/tts stats`, `status`, `diagnostics`, `disconnect`, `clear` |
| **DALL·E** | `/image` — DALL·E 3 generation with queue and cache |
| **Secret Santa** | `/ss start`, `status`, `shuffle`, `stop`, `ask_giftee`, `giftee`, `wishlist`, `submit_gift`, `oversight`, `history`, `edit_gift`, `archive`, `user_history`; reaction signup; Reply button |
| **Distribute** | `/distribute upload` (attach file), `list`, `browse`, `get`, `remove` |

## Prerequisites

1. [.NET 8 SDK](https://dotnet.microsoft.com/download)
2. Fluxer bot token ([developer dashboard](https://web.fluxer.app))
3. OpenAI API key (TTS, DALL·E, SS anonymization)
4. **ffmpeg** on PATH (or `FFMPEG_PATH`)
5. Optional: mod role ID, log channel ID

## Configuration

Copy `WaveTechFluxerTTS/appsettings.example.json` → `appsettings.json`:

| Key | Env fallback |
|-----|----------------|
| `Fluxer:BotToken` | `FLUXER_BOT_TOKEN` |
| `OpenAI:ApiKey` | `OPENAI_API_KEY` |
| `Fluxer:LogChannelId` | `FLUXER_LOG_CHANNEL_ID` |
| `Fluxer:ModeratorRoleId` | `FLUXER_MODERATOR_ROLE_ID` |

## Data migration (from Python bot)

Copy into `WaveTechFluxerTTS/Data/` (or set `Data:Root`):

```
Data/
  secret_santa_state.json
  archive/
  distributed_files/
  distributed_files_metadata.json
```

## Run (local)

```powershell
cd WaveTechFluxerTTS
dotnet run
```

Open `WaveTechFluxerTTS.sln` in Visual Studio 2022+.

## Run (Docker)

```powershell
copy config.env.example config.env
# Edit config.env — FLUXER_BOT_TOKEN and OPENAI_API_KEY at minimum
deploy.bat
```

```bash
cp config.env.example config.env && ./deploy    # Linux / macOS / NAS
```

| Command | What it does |
|---------|----------------|
| `docker compose up -d --build` | Start bot in background |
| `docker compose logs -f` | Follow logs |
| `docker compose down` | Stop and remove container |

Bot data (Secret Santa, distributed files) persists in `./data` on the host.

## Architecture

Modules implement `IBotModule` and register via `BotHost`. Gateway handles messages, voice, reactions, and interactions. Voice uses LiveKit (not Discord Opus/DAVE).
