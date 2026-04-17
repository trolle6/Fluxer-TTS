"""
FluxerTools – Professional Fluxer Bot

TTS (OpenAI), DALL-E 3 images, Secret Santa.

Usage: python main.py
Fluxer: https://fluxer.app | https://docs.fluxer.app
"""

# Re-run with project venv if PyCharm/IDE used wrong interpreter
if __name__ == "__main__":
    import os
    import sys
    import subprocess
    import signal
    _script_dir = os.path.dirname(os.path.abspath(__file__))
    _venv_py = os.path.join(_script_dir, ".venv", "Scripts" if os.name == "nt" else "bin", "python.exe" if os.name == "nt" else "python")
    if os.path.exists(_venv_py):
        _current = os.path.realpath(sys.executable)
        _wanted = os.path.realpath(_venv_py)
        if _current != _wanted:
            # Use Popen so we can forward Ctrl+C to child and wait for it.
            proc = subprocess.Popen([_venv_py] + sys.argv)
            try:
                sys.exit(proc.wait())
            except KeyboardInterrupt:
                proc.terminate()
                try:
                    proc.wait(timeout=8)
                except subprocess.TimeoutExpired:
                    proc.kill()
                    proc.wait()
                sys.exit(130)

import asyncio
import difflib
import io
import logging
import logging.handlers
import os
import queue
import signal
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import Any, Optional

import aiohttp
import fluxer
from fluxer.models import VoiceState
from dotenv import load_dotenv

load_dotenv("config.env", override=True)


class FluxerToolsBot(fluxer.Bot):
    """Custom Bot that populates voice states from GUILD_CREATE.

    Fluxer's client only caches voice states from VOICE_STATE_UPDATE events.
    If a user is already in voice when the bot starts, the bot never sees that
    event. GUILD_CREATE includes voice_states — we populate the cache from it.
    """

    async def _dispatch(self, event_name: str, data: Any) -> None:
        if event_name == "GUILD_CREATE" and self._http:
            guild_id = int(data["id"]) if data.get("id") else 0
            if guild_id:
                self._voice_states.pop(guild_id, None)
                for vs_data in data.get("voice_states", []):
                    try:
                        vs = VoiceState.from_data(vs_data, self._http)
                        if vs.guild_id is not None and vs.channel_id is not None:
                            guild_states = self._voice_states.setdefault(vs.guild_id, {})
                            guild_states[vs.user_id] = vs
                    except Exception:
                        pass
        await super()._dispatch(event_name, data)

# ============ CONFIG ============
REQUIRED_CONFIG_KEYS = {
    "FLUXER_TOKEN", "FLUXER_GUILD_ID", "FLUXER_CHANNEL_ID",
    "FLUXER_LOG_CHANNEL_ID", "FLUXER_MODERATOR_ROLE_ID", "OPENAI_API_KEY"
}

CONFIG_DEFAULTS = {
    "DEBUG_MODE": False,
    "LOG_LEVEL": "INFO",
    "SKIP_API_VALIDATION": False,
    "MAX_TTS_CACHE": 50,
    "TTS_TIMEOUT": 15,
    "MAX_QUEUE_SIZE": 50,
    "RATE_LIMIT_REQUESTS": 15,
    "RATE_LIMIT_WINDOW": 60,
    "VOICE_TIMEOUT": 10,
    "AUTO_DISCONNECT_TIMEOUT": 300,
    "TTS_ROLE_ID": None,
    "BOT_OWNER_USER_ID": None,
    # Comma-separated user IDs allowed to run Secret Santa mod commands (if API omits roles)
    "FLUXER_MODERATOR_USER_IDS": None,
    # Optional: role display name to match (case-insensitive) in addition to FLUXER_MODERATOR_ROLE_ID
    "FLUXER_MODERATOR_ROLE_NAME": None,
    "TTS_MAX_MESSAGE_LENGTH": 4096,  # OpenAI TTS max; Fluxer may truncate earlier
}


class Config:
    """Configuration loader with validation."""

    def __init__(self):
        self.data: dict[str, Any] = {}
        missing = [key for key in REQUIRED_CONFIG_KEYS if not os.getenv(key)]
        if missing:
            raise RuntimeError(f"Missing required config: {', '.join(missing)}")

        for key in REQUIRED_CONFIG_KEYS:
            val = os.getenv(key)
            self.data[key] = val.strip() if isinstance(val, str) else val

        for key, default in CONFIG_DEFAULTS.items():
            val = os.getenv(key)
            if val is None:
                self.data[key] = default
            elif isinstance(default, bool):
                self.data[key] = str(val).lower() == "true"
            elif isinstance(default, int):
                try:
                    self.data[key] = int(val)
                except ValueError:
                    self.data[key] = default
            elif key == "BOT_OWNER_USER_ID" and val:
                try:
                    self.data[key] = int(val)
                except ValueError:
                    self.data[key] = None
            elif key == "FLUXER_MODERATOR_USER_IDS":
                if not val or not str(val).strip():
                    self.data[key] = None
                else:
                    ids: list[int] = []
                    for part in str(val).split(","):
                        p = part.strip()
                        if p.isdigit():
                            ids.append(int(p))
                    self.data[key] = ids if ids else None
            else:
                self.data[key] = val

    def __getattr__(self, name: str) -> Any:
        key = name.upper()
        if key in self.data:
            return self.data[key]
        return CONFIG_DEFAULTS.get(key)


# ============ HTTP MANAGER ============
HTTP_CONNECTION_LIMIT = 10
HTTP_CONNECTION_LIMIT_PER_HOST = 5
HTTP_DNS_CACHE_TTL = 300
HTTP_DEFAULT_TIMEOUT = 30


class HttpManager:
    """Singleton HTTP session manager."""

    _instance = None
    _session: Optional[aiohttp.ClientSession] = None

    def __new__(cls):
        if cls._instance is None:
            cls._instance = super().__new__(cls)
        return cls._instance

    async def get_session(self, timeout: int = HTTP_DEFAULT_TIMEOUT) -> aiohttp.ClientSession:
        try:
            current_loop = asyncio.get_running_loop()
        except RuntimeError:
            current_loop = None
        session_loop = getattr(self._session, "_loop", None) if self._session else None
        need_new = (
            self._session is None or self._session.closed or session_loop is None
            or session_loop.is_closed()
            or (current_loop and session_loop != current_loop)
        )
        if need_new:
            if self._session and not self._session.closed:
                try:
                    await self._session.close()
                except Exception:
                    pass
                self._session = None
            connector = aiohttp.TCPConnector(
                limit=HTTP_CONNECTION_LIMIT,
                limit_per_host=HTTP_CONNECTION_LIMIT_PER_HOST,
                ttl_dns_cache=HTTP_DNS_CACHE_TTL,
                enable_cleanup_closed=True,
            )
            self._session = aiohttp.ClientSession(
                timeout=aiohttp.ClientTimeout(total=timeout),
                connector=connector,
                headers={"Connection": "keep-alive"},
            )
        return self._session

    async def invalidate_session(self):
        if self._session and not self._session.closed:
            try:
                await self._session.close()
            except Exception:
                pass
        self._session = None

    async def close(self):
        if self._session and not self._session.closed:
            try:
                await self._session.close()
                await asyncio.sleep(0.5)
            except Exception:
                pass
        self._session = None


# ============ LOGGING ============
LOG_FILE_MAX_BYTES = 5_000_000
LOG_FILE_BACKUP_COUNT = 5


# Sentinel to unblock queue.get() in the log sender thread (otherwise a ThreadPool
# worker blocks forever and asyncio.run() cannot shut down the default executor).
_LOG_SENDER_STOP = object()


class FluxerLogHandler(logging.Handler):
    """Send log messages to Fluxer channel. Uses queue.Queue (not asyncio) so emit()
    works during shutdown when the event loop may be closed."""

    EMOJI_MAP = {"WARNING": "⚠️", "ERROR": "❌", "CRITICAL": "🚨"}

    def __init__(self, log_channel_id: int):
        super().__init__()
        self.log_channel_id = log_channel_id
        self.bot: Optional[fluxer.Bot] = None
        self.message_queue: queue.Queue = queue.Queue(maxsize=50)
        self.sender_task: Optional[asyncio.Task] = None
        self._last_message: dict = {}

    def set_bot(self, bot: fluxer.Bot):
        self.bot = bot
        if not self.sender_task:
            self.sender_task = asyncio.create_task(self._sender_loop())

    def request_stop(self) -> None:
        """Unblock the sender thread so the process can exit (call before loop closes)."""
        try:
            self.message_queue.put_nowait(_LOG_SENDER_STOP)
        except queue.Full:
            try:
                self.message_queue.get_nowait()
            except queue.Empty:
                pass
            try:
                self.message_queue.put_nowait(_LOG_SENDER_STOP)
            except queue.Full:
                pass

    def emit(self, record: logging.LogRecord):
        if not self.bot or not self.log_channel_id or record.levelno < logging.WARNING:
            return
        msg_key = f"{record.levelname}:{record.getMessage()[:50]}"
        now = time.time()
        if msg_key in self._last_message and (now - self._last_message[msg_key]) < 60:
            return
        self._last_message[msg_key] = now
        emoji = self.EMOJI_MAP.get(record.levelname, "ℹ️")
        message = f"{emoji} **{record.levelname}** | {record.name}\n```\n{record.getMessage()}\n```"
        if len(message) > 1900:
            message = message[:1900] + "...\n```"
        try:
            self.message_queue.put_nowait(message)
        except queue.Full:
            pass

    async def _sender_loop(self):
        loop = asyncio.get_running_loop()
        while True:
            try:
                msg = await loop.run_in_executor(None, self.message_queue.get)
                if msg is _LOG_SENDER_STOP:
                    break
                if self.bot and self.log_channel_id:
                    try:
                        channel = await self.bot.fetch_channel(str(self.log_channel_id))
                        if channel:
                            await channel.send(msg)
                    except Exception:
                        pass
                await asyncio.sleep(1)
            except asyncio.CancelledError:
                break
            except Exception:
                continue


def setup_logging(config: Config) -> tuple[logging.Logger, FluxerLogHandler]:
    logger = logging.getLogger("bot")
    logger.setLevel(config.LOG_LEVEL)
    # Reduce "Invalid session" spam from fluxer (expected when zombie sessions exist)
    logging.getLogger("fluxer.gateway").setLevel(logging.ERROR)
    if logger.handlers:
        for h in logger.handlers:
            if isinstance(h, FluxerLogHandler):
                return logger, h

    fmt = logging.Formatter("%(asctime)s - %(name)s - %(levelname)s - %(message)s", datefmt="%Y-%m-%d %H:%M:%S")
    fh = logging.handlers.RotatingFileHandler("bot.log", maxBytes=LOG_FILE_MAX_BYTES, backupCount=LOG_FILE_BACKUP_COUNT, encoding="utf-8")
    fh.setFormatter(fmt)
    logger.addHandler(fh)
    utf8_stream = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace") if hasattr(sys.stdout, "buffer") else sys.stdout
    ch = logging.StreamHandler(utf8_stream)
    ch.setFormatter(fmt)
    logger.addHandler(ch)
    fluxer_handler = FluxerLogHandler(int(config.FLUXER_LOG_CHANNEL_ID))
    fluxer_handler.setLevel(logging.WARNING)
    logger.addHandler(fluxer_handler)
    return logger, fluxer_handler


# ============ OPENAI VALIDATION ============
OPENAI_VALIDATION_URL = "https://api.openai.com/v1/models"
OPENAI_VALIDATION_TIMEOUT = 10


async def validate_openai_key(key: str, logger: logging.Logger, http_mgr: HttpManager) -> bool:
    key = key.strip() if key else ""
    if not key:
        logger.error("OPENAI_API_KEY is empty")
        return False
    if not key.startswith("sk-"):
        logger.error("Invalid API key format (should start with 'sk-')")
        return False
    try:
        session = await http_mgr.get_session()
        async with session.get(OPENAI_VALIDATION_URL, headers={"Authorization": f"Bearer {key}"}, timeout=aiohttp.ClientTimeout(total=OPENAI_VALIDATION_TIMEOUT)) as r:
            if r.status == 200:
                logger.info("OpenAI API key is valid")
                return True
            elif r.status == 401:
                logger.error("API key is invalid or expired")
                return False
            else:
                logger.warning(f"Unexpected API response: {r.status}")
                return True
    except Exception as e:
        logger.warning(f"API validation error: {e}")
        return True


# ============ BOT SETUP ============
try:
    config = Config()
except RuntimeError as e:
    print(f"Fatal: {e}")
    sys.exit(1)

logger, fluxer_handler = setup_logging(config)

# Optional: use different Fluxer instance (e.g. self-hosted)
api_url = os.getenv("FLUXER_API_URL", None)
bot = FluxerToolsBot(command_prefix="/", intents=fluxer.Intents.all(), api_url=api_url)
bot.config = config
bot.logger = logger
bot.http_mgr = HttpManager()
bot.executor = ThreadPoolExecutor(max_workers=4, thread_name_prefix="bot-io")
bot.fluxer_handler = fluxer_handler
bot.ready_once = False

LEVEL_EMOJIS = {"INFO": "ℹ️", "WARNING": "⚠️", "ERROR": "❌", "CRITICAL": "🚨", "SUCCESS": "✅"}


async def send_fluxer_message(channel_id: int, message: str, level: str = "INFO", include_level: bool = True):
    if not bot.ready_once:
        return
    try:
        channel = await bot.fetch_channel(str(channel_id))
        if not channel:
            return
        emoji = LEVEL_EMOJIS.get(level, "ℹ️")
        formatted = f"{emoji} **{level}** | {message}" if include_level else f"{emoji} {message}"
        if len(formatted) > 2000:
            formatted = formatted[:1997] + "..."
        await channel.send(formatted)
    except Exception:
        pass


async def send_to_log(message: str, level: str = "INFO"):
    await send_fluxer_message(int(config.FLUXER_LOG_CHANNEL_ID), message, level)


async def send_to_channel(message: str, level: str = "INFO"):
    await send_fluxer_message(int(config.FLUXER_CHANNEL_ID), message, level, include_level=False)


bot.send_to_log = send_to_log
bot.send_to_channel = send_to_channel


# ============ EVENTS ============
@bot.event
async def on_message(message):
    """Debug logging + lightweight command suggestions for prefix UX."""
    if getattr(message.author, "bot", False):
        return
    content = getattr(message, "content", "") or ""
    prefix = bot.command_prefix if isinstance(bot.command_prefix, str) else "/"
    if prefix and content.startswith(prefix):
        after = content[len(prefix):].strip()
        if after:
            cmd = after.split()[0].lower()
            known = list(getattr(bot, "_commands", {}).keys())
            if cmd and cmd not in known:
                guesses = difflib.get_close_matches(cmd, known, n=3, cutoff=0.5)
                if guesses:
                    hint = " · ".join(f"`{prefix}{g}`" for g in guesses)
                    try:
                        await message.reply(f"Unknown command `{prefix}{cmd}`. Try: {hint}")
                    except Exception:
                        pass

    if getattr(config, "DEBUG_MODE", False):
        logger.info("MSG: ch=%s guild=%s author=%s: %r", getattr(message, "channel_id", "?"), getattr(message, "guild_id", "?"), getattr(getattr(message, "author", None), "username", "?"), (getattr(message, "content", "") or "")[:80])


@bot.event
async def on_ready():
    logger.info(f"Logged in as {bot.user.username}")
    if fluxer_handler:
        fluxer_handler.set_bot(bot)
    try:
        await bot.http_mgr.invalidate_session()
    except Exception:
        pass
    # Load cogs on first ready (fluxer.load_extension is async)
    if not bot.ready_once:
        num = await load_cogs()
        if num == 0:
            logger.error("No cogs loaded!")
    try:
        channel = await bot.fetch_channel(str(config.FLUXER_LOG_CHANNEL_ID))
        if channel:
            await channel.send(f"🤖 **Bot Online** | {bot.user.username} is ready!")
    except Exception:
        pass
    bot.ready_once = True


@bot.event
async def on_error(event, *args, **kwargs):
    logger.error(f"Error in {event}", exc_info=True)


# ============ GRACEFUL SHUTDOWN ============
_shutdown_in_progress = False


async def _do_shutdown():
    """Actual shutdown logic. Called with timeout so we never hang forever."""
    global _shutdown_in_progress
    if _shutdown_in_progress:
        return
    _shutdown_in_progress = True
    logger.info("Shutting down...")

    # 1. Stop Fluxer log handler sender first — unblock queue.get() then end task
    if fluxer_handler:
        fluxer_handler.request_stop()
        if fluxer_handler.sender_task and not fluxer_handler.sender_task.done():
            try:
                await asyncio.wait_for(fluxer_handler.sender_task, timeout=3.0)
            except (asyncio.CancelledError, asyncio.TimeoutError):
                fluxer_handler.sender_task.cancel()
                try:
                    await fluxer_handler.sender_task
                except asyncio.CancelledError:
                    pass
    logger.info("Shutdown: log handler stopped")

    # 2. Disconnect voice FIRST (before removing cogs) - LiveKit can block
    voice_cog = bot.get_cog("VoiceProcessingCog")
    if voice_cog and hasattr(voice_cog, "_active_voice") and voice_cog._active_voice:
        logger.info("Shutdown: disconnecting %d voice connection(s)...", len(voice_cog._active_voice))
        for guild_id, vc in list(voice_cog._active_voice.items()):
            voice_cog._active_voice.pop(guild_id, None)
            try:
                await asyncio.wait_for(vc.disconnect(), timeout=2.0)
            except (asyncio.TimeoutError, Exception) as e:
                logger.debug("Shutdown: voice disconnect %s: %s", guild_id, e)
        logger.info("Shutdown: voice disconnected")

    # 3. Remove cogs (lightweight now - voice already disconnected)
    for cog_name in list(bot._cogs.keys()):
        try:
            await asyncio.wait_for(bot.remove_cog(cog_name), timeout=3.0)
            logger.info("Shutdown: removed cog %s", cog_name)
        except asyncio.TimeoutError:
            logger.warning("Shutdown: cog %s unload timed out", cog_name)
        except Exception as e:
            logger.warning("Shutdown: cog %s error: %s", cog_name, e)
    logger.info("Shutdown: cogs removed")

    await asyncio.sleep(0.3)

    try:
        await asyncio.wait_for(bot.http_mgr.close(), timeout=3.0)
    except Exception:
        pass

    try:
        await asyncio.wait_for(bot.close(), timeout=5.0)
        logger.info("Shutdown: bot closed")
    except Exception as e:
        logger.warning("Shutdown: bot.close error: %s", e)


async def graceful_shutdown():
    """Shutdown with overall timeout - never hang forever."""
    try:
        await asyncio.wait_for(_do_shutdown(), timeout=12.0)
    except asyncio.TimeoutError:
        logger.warning("Shutdown timed out, forcing exit")


def handle_signal(signum, frame):
    logger.info(f"Received signal {signum}")
    try:
        loop = asyncio.get_running_loop()
        loop.create_task(graceful_shutdown())
    except RuntimeError:
        pass


# ============ COG LOADING ============
COG_EXTENSIONS = [
    "cogs.DALLE_cog",
    "cogs.voice_processing_cog",
    "cogs.SecretSanta_cog",
]


async def load_cogs() -> int:
    """Load all cogs (async - call from on_ready)."""
    loaded = 0
    for cog in COG_EXTENSIONS:
        try:
            await bot.load_extension(cog)
            logger.info(f"Loaded {cog}")
            loaded += 1
        except Exception as e:
            logger.error(f"Failed to load {cog}: {e}")
    return loaded


# ============ MAIN ============
if __name__ == "__main__":
    logger.info("Starting FluxerTools...")
    logger.info("Tip: If connection is slow or you see multiple 'devices', revoke & regenerate your token in Fluxer → Applications to clear zombie sessions.")
    PYTHON_MIN = (3, 10)
    if sys.version_info < PYTHON_MIN:
        logger.critical(f"Python {PYTHON_MIN[0]}.{PYTHON_MIN[1]}+ required.")
        sys.exit(1)

    REQUIRED_DIRS = ["cogs/archive", "cogs/archive/backups"]
    for d in REQUIRED_DIRS:
        Path(d).mkdir(parents=True, exist_ok=True)

    if not config.SKIP_API_VALIDATION:
        if not asyncio.run(validate_openai_key(config.OPENAI_API_KEY, logger, bot.http_mgr)):
            logger.critical("OpenAI API key is invalid.")
            sys.exit(1)
        asyncio.run(bot.http_mgr.invalidate_session())

    logger.info("Starting bot (cogs load on connect)...")

    for sig in (signal.SIGINT, signal.SIGTERM):
        try:
            signal.signal(sig, handle_signal)
        except (ValueError, OSError):
            pass

    try:
        bot.run(config.FLUXER_TOKEN)
        logger.info("Bot has stopped (gateway disconnected)")
    except KeyboardInterrupt:
        logger.info("Keyboard interrupt")
    except Exception as e:
        logger.critical(f"Bot crashed: {e}", exc_info=True)
        raise
    finally:
        # ThreadPoolExecutor used by cogs must shut down here — _do_shutdown may not
        # run on KeyboardInterrupt-only exit. Second shutdown is a no-op in recent Python.
        ex = getattr(bot, "executor", None)
        if ex is not None:
            try:
                ex.shutdown(wait=True, cancel_futures=True)
            except TypeError:
                ex.shutdown(wait=True)
            except RuntimeError:
                pass

    # Optional: keep window open on Windows when run by double-click (no console)
    if os.name == "nt" and os.environ.get("PAUSE_ON_EXIT", "").lower() == "true":
        try:
            input("\nPress Enter to close...")
        except (EOFError, UnicodeDecodeError, OSError, KeyboardInterrupt):
            pass
