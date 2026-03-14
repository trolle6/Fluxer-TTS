# FluxerTools

A **Fluxer**-friendly bot ported from [WaveTechToolBoxx](https://github.com/trolle6/WaveTechToolBoxx) (Discord).

## Features

- **DALL-E 3** – AI image generation with queue and caching
- **TTS** – Text-to-speech in voice channels via OpenAI
- **Secret Santa** – Event management (start, join, shuffle, wishlists, gifts, history, DMs)

## Requirements

- Python 3.10+
- [Fluxer](https://fluxer.app) account and community
- [OpenAI API key](https://platform.openai.com/api-keys) (for TTS and DALL-E)
- **ffmpeg** installed (for voice/TTS)

## Quick Start

1. **Clone and setup** (first time only)

   **Windows:** Double-click `setup.bat` or run:
   ```cmd
   setup.bat
   ```

   **macOS/Linux:** Run `./setup.sh` or:
   ```bash
   cd FluxerTools
   python3 -m venv .venv
   .venv/bin/pip install -r requirements.txt
   ```

   **Or manual:** `pip install -r requirements.txt` (in your project venv)

2. **Configure**

   Copy `config.env.example` to `config.env` and fill in:

   - `FLUXER_TOKEN` – Create at [Fluxer User Settings → Applications](https://web.fluxer.app)
   - `FLUXER_GUILD_ID` – Your community ID
   - `FLUXER_CHANNEL_ID` – Main channel for bot messages
   - `FLUXER_LOG_CHANNEL_ID` – Log channel
   - `FLUXER_MODERATOR_ROLE_ID` – Moderator role for admin commands
   - `OPENAI_API_KEY` – Your OpenAI API key

3. **Run**

   | Platform | Command |
   |----------|---------|
   | Windows | `run.bat` or `\.venv\Scripts\python.exe main.py` |
   | macOS / Linux | `./run.sh` or `./.venv/bin/python main.py` |
   | Docker (any) | `docker compose up -d` |
   | PyCharm | Set interpreter to `FluxerTools\.venv\Scripts\python.exe` |

## Running on NAS

### Option A: Docker (recommended for Synology, QNAP, etc.)

1. Copy the project to your NAS.
2. Edit `config.env` with your tokens/IDs.
3. Run:

   ```bash
   docker compose up -d --build
   ```

   Or use the helper script: `./deploy`

   Logs: `docker compose logs -f`  
   Stop: `docker compose down`

### Option B: Python + systemd (Linux NAS with SSH)

1. Install Python 3.10+, ffmpeg, and dependencies.
2. Create a systemd unit (e.g. `/etc/systemd/system/fluxertools.service`):

   ```ini
   [Unit]
   Description=FluxerTools Bot
   After=network.target

   [Service]
   Type=simple
   User=youruser
   WorkingDirectory=/path/to/FluxerTools
   ExecStart=/path/to/FluxerTools/.venv/bin/python main.py
   Restart=always
   RestartSec=10

   [Install]
   WantedBy=multi-user.target
   ```

3. Enable and start: `sudo systemctl enable --now fluxertools`

## Commands

All commands use the `!` prefix.

| Command | Description |
|---------|-------------|
| `!image <prompt> [size] [quality]` | Generate an image with DALL-E 3. Sizes: `1024x1024`, `1792x1024`, `1024x1792`. Quality: `standard`, `hd`. |
| **TTS** | No command needed. Type in the TTS channel (default: main channel) while in voice — the bot speaks your message. |
| `!ss help` | Secret Santa help |
| `!ss start` | Start event (moderator) |
| `!ss join` / `!ss leave` | Sign up / leave |
| `!ss shuffle` | Make assignments & DM participants (moderator) |
| `!ss stop` | End & archive (moderator) |
| `!ss wishlist add/remove/view/clear` | Wishlist management |
| `!ss giftee` | See your giftee's wishlist |
| `!ss ask_giftee <q>` / `!ss reply_santa <r>` | Anonymous Q&A |
| `!ss submit_gift <desc>` | Record gift |
| `!ss history [year]` / `!ss user_history @user` | Archives |

## Fluxer vs Discord

[Fluxer](https://docs.fluxer.app) is a Discord-like platform. This bot uses [fluxer.py](https://pypi.org/project/fluxer.py/). See `FLUXER_CAPABILITIES.md` for API reference.

Key differences:

- **Prefix commands** (`!command`) – Fluxer does not yet support slash commands
- **Fluxer REST/WebSocket APIs** – Similar to Discord but with different endpoints
- **LiveKit** for voice – Voice uses LiveKit instead of Discord’s voice stack

## Project Structure

```
FluxerTools/
├── main.py                  # Bot entry point (FluxerToolsBot with voice_state fix)
├── config.env               # Your config (create from config.env.example)
├── requirements.txt
├── run.bat / run.sh         # Cross-platform run scripts
├── setup.bat / setup.sh     # First-time setup
├── FLUXER_CAPABILITIES.md   # Fluxer API reference
├── cogs/
│   ├── DALLE_cog.py         # DALL-E 3 image generation (queue, cache)
│   ├── voice_processing_cog.py  # Auto-TTS (stays in VC until empty)
│   ├── fluxy.py             # Fluxer-native styling (embeds, colors)
│   ├── SecretSanta_cog.py   # Secret Santa (full event management)
│   ├── secret_santa_storage.py
│   ├── secret_santa_assignments.py
│   └── utils.py             # RateLimiter, LRUCache, CircuitBreaker
└── cogs/archive/
```

## License

Original WaveTechToolBoxx: see [its repository](https://github.com/trolle6/WaveTechToolBoxx).  
This port: adapt as needed for your use.
