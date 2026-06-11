# Leave Discord — Fluxer migration checklist

Use this to switch from [WaveTechToolBoxx](https://github.com/trolle6/WaveTechToolBoxx) (Discord) to **WaveTech Fluxer Toolbox** (Fluxer).

## 1. Create Fluxer bot

1. Open Fluxer developer dashboard → create application / bot.
2. Copy bot token → `Fluxer:BotToken` or env `FLUXER_BOT_TOKEN`.
3. Invite bot to your community with permissions: Send Messages, Manage Roles (for SS), Connect/Speak (for TTS voice).

## 2. Copy your data

From the Python bot folder `cogs/` into `Data/` next to the exe:

```
Data/secret_santa_state.json
Data/archive/          (all YYYY.json files)
Data/distributed_files/
Data/distributed_files_metadata.json
```

## 3. Configure

Copy `appsettings.example.json` → `appsettings.json`:

```json
{
  "Fluxer": {
    "BotToken": "YOUR_TOKEN",
    "LogChannelId": "YOUR_LOG_CHANNEL",
    "ModeratorRoleId": "YOUR_MOD_ROLE"
  },
  "OpenAI": { "ApiKey": "YOUR_OPENAI_KEY" },
  "Tts": { "AllowedChannelId": "OPTIONAL_TEXT_CHANNEL_FOR_TTS" }
}
```

## 4. Install ffmpeg

TTS requires ffmpeg on PATH (or set `FFMPEG_PATH`).

```powershell
winget install Gyan.FFmpeg
```

## 5. Run

```powershell
cd WaveTechFluxerTTS\WaveTechFluxerTTS
dotnet run
```

Slash commands register on startup. Check log channel for "online" message.

## 6. Feature parity (what works on Fluxer)

| Feature | Status |
|---------|--------|
| Auto-TTS in voice | Yes (LiveKit, not Discord voice) |
| `/tts *` commands | Yes |
| `/image` DALL·E | Yes |
| Secret Santa full flow | Yes (start → react → shuffle → ask/reply → stop) |
| `/ss edit_gift`, `/ss archive`, `/ss user_history` | Yes |
| `/distribute upload/get` | Yes (attach file to slash command) |
| Scheduled auto-shuffle/stop | Not yet — run `/ss shuffle` and `/ss stop` manually |
| Docker deploy | Not yet — run `dotnet run` or publish exe |

## 7. Turn off Discord bot

Only after Fluxer bot is tested:

1. Stop Python `main.py` / Docker container.
2. Revoke or rotate `DISCORD_TOKEN` if you want to be sure nothing uses it.

## 8. Quick test

- [ ] Join voice, send chat → hear TTS
- [ ] `/image` with short prompt
- [ ] `/ss start` → react on signup → `/ss shuffle`
- [ ] `/distribute upload` with a small zip attached
