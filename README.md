# WaveTech Fluxer Toolbox

Fluxer port of [WaveTechToolBoxx](https://github.com/trolle6/WaveTechToolBoxx) — TTS, DALL·E, Secret Santa, and file distribution.

**Primary implementation:** C# / .NET 8 (`WaveTechFluxerTTS.sln`) — slash commands, full feature set including `/distribute`.

**Legacy:** Python + `fluxer.py` with `!` prefix commands lives in [`legacy/python/`](legacy/python/) (Docker deploy still works from that folder).

**Leaving Discord?** See [LEAVE_DISCORD.md](LEAVE_DISCORD.md).

## Features (C# bot)

| Module | Commands / behavior |
|--------|---------------------|
| **TTS** | Auto-TTS in voice; `/tts stats`, `status`, `diagnostics`, `disconnect`, `clear` |
| **DALL·E** | `/image` — DALL·E 3 generation with queue and cache |
| **Secret Santa** | `/ss start`, `status`, `shuffle`, `stop`, `ask_giftee`, `giftee`, `wishlist`, `submit_gift`, `oversight`, `history`, `edit_gift`, `archive`, `user_history`; reaction signup; Reply button |
| **Distribute** | `/distribute upload` (attach file), `list`, `browse`, `get`, `remove` |

## Prerequisites (C#)

1. [.NET 8 SDK](https://dotnet.microsoft.com/download)
2. Fluxer bot token ([developer dashboard](https://web.fluxer.app))
3. OpenAI API key (TTS, DALL·E, SS anonymization)
4. **ffmpeg** on PATH (or `FFMPEG_PATH`)
5. Optional: mod role ID, log channel ID

## Quick start (C#)

```powershell
cd WaveTechFluxerTTS
copy appsettings.example.json appsettings.json
# Edit appsettings.json — set Fluxer:BotToken and OpenAI:ApiKey
dotnet run
```

Open `WaveTechFluxerTTS.sln` in Visual Studio 2022+.

| Key | Env fallback |
|-----|----------------|
| `Fluxer:BotToken` | `FLUXER_BOT_TOKEN` |
| `OpenAI:ApiKey` | `OPENAI_API_KEY` |
| `Fluxer:LogChannelId` | `FLUXER_LOG_CHANNEL_ID` |
| `Fluxer:ModeratorRoleId` | `FLUXER_MODERATOR_ROLE_ID` |

Slash commands register on startup.

## Data migration (from Discord or Python bot)

Copy into `WaveTechFluxerTTS/Data/` (or set `Data:Root` in config):

```
Data/
  secret_santa_state.json
  archive/
  distributed_files/
  distributed_files_metadata.json
```

From the old Python bots, source paths are `cogs/secret_santa_state.json`, `cogs/archive/`, etc.

## Legacy Python bot (Docker / NAS)

```bash
cd legacy/python
cp config.env.example config.env
# edit config.env
docker compose --env-file config.env up -d --build
```

See `legacy/python/README.md` for `!` prefix commands and PyCharm setup.

## Architecture (C#)

Modules implement `IBotModule` and register via `BotHost`. Gateway handles messages, voice, reactions, and interactions. Voice uses LiveKit (not Discord Opus/DAVE).

## License

Original WaveTechToolBoxx: see [its repository](https://github.com/trolle6/WaveTechToolBoxx).
