"""
Voice Processing Cog - TTS for Fluxer
Auto-TTS: when you type in the TTS channel, the bot speaks it in voice.

FEATURES:
- 🎤 Auto-TTS: type in the designated channel → bot speaks in your voice channel
- 🤖 OpenAI TTS API
- ⚡ Basic caching

Configure TTS channel via FLUXER_CHANNEL_ID (or TTS_CHANNEL_ID to use a different channel).
"""

import asyncio
import hashlib
import re
import tempfile
from pathlib import Path
from typing import Optional

import aiohttp
import fluxer
from fluxer.cog import Cog

from . import utils

TTS_MAX_CHARS = 4096
TTS_URL = "https://api.openai.com/v1/audio/speech"
DEFAULT_VOICE = "alloy"


class VoiceProcessingCog(Cog):
    """Auto-TTS: speaks messages typed in the TTS channel when author is in voice."""

    def __init__(self, bot: fluxer.Bot):
        super().__init__(bot)
        self.logger = bot.logger.getChild("voice")

        if not getattr(bot.config, "OPENAI_API_KEY", None):
            self.logger.warning("OPENAI_API_KEY not configured - TTS disabled")
            self.enabled = False
            return

        self.enabled = True
        self.rate_limiter = utils.RateLimiter(
            limit=bot.config.RATE_LIMIT_REQUESTS,
            window=bot.config.RATE_LIMIT_WINDOW,
        )
        self.cache = utils.LRUCache[bytes](max_size=bot.config.MAX_TTS_CACHE, ttl=3600)
        self._emoji_pattern = re.compile(r"<(a?):([\w-]+):\d+>")
        self._cleanup_pattern = re.compile(r"<@!?\d+>|<@&\d+>|<#\d+>|https?://\S+")
        self._playing_guilds: set[int] = set()
        self._playing_lock = asyncio.Lock()
        self._active_voice: dict[int, fluxer.VoiceClient] = {}  # guild_id -> VoiceClient (stays until VC empty)
        # TTS channel: use TTS_CHANNEL_ID if set, else FLUXER_CHANNEL_ID
        tts_ch = getattr(bot.config, "TTS_CHANNEL_ID", None) or getattr(bot.config, "FLUXER_CHANNEL_ID", None)
        self._tts_channel_id = int(tts_ch) if tts_ch else None
        self.logger.info("TTS cog initialized (channel=%s)", self._tts_channel_id)

    def _clean_text(self, text: str) -> str:
        """Remove Discord/Fluxer formatting from text."""
        text = self._emoji_pattern.sub(r"\2", text)
        text = self._cleanup_pattern.sub("", text)
        return " ".join(text.split()).strip()

    def _cache_key(self, text: str, voice: str) -> str:
        return hashlib.sha256(f"{text}:{voice}".encode()).hexdigest()

    async def _generate_tts(self, text: str, voice: str = DEFAULT_VOICE) -> Optional[bytes]:
        """Call OpenAI TTS API. Returns MP3 bytes or None."""
        headers = {
            "Authorization": f"Bearer {self.bot.config.OPENAI_API_KEY}",
            "Content-Type": "application/json",
        }
        payload = {
            "model": "tts-1-hd",
            "input": text[:TTS_MAX_CHARS],
            "voice": voice,
            "response_format": "mp3",
        }
        try:
            session = await self.bot.http_mgr.get_session()
            async with session.post(TTS_URL, json=payload, headers=headers, timeout=aiohttp.ClientTimeout(total=60)) as r:
                if r.status == 200:
                    return await r.read()
        except Exception as e:
            self.logger.error(f"TTS API error: {e}")
        return None

    def _is_alone_in_channel(self, guild_id: int, channel_id: int) -> bool:
        """True if only the bot is in this voice channel (or channel is empty)."""
        bot_id = self.bot.user.id if self.bot.user else None
        if not bot_id:
            return True
        states = self.bot.get_guild_voice_states(guild_id)
        others = [s for s in states if s.channel_id == channel_id and s.user_id != bot_id]
        return len(others) == 0

    async def _maybe_disconnect_if_alone(self, guild_id: int) -> None:
        """If we're in a voice channel and are the only one left, disconnect."""
        vc = self._active_voice.get(guild_id)
        if not vc or not vc.is_connected:
            return
        if self._is_alone_in_channel(guild_id, vc.channel_id):
            self._active_voice.pop(guild_id, None)
            try:
                await vc.disconnect()
                self.logger.info("TTS left VC (channel empty): guild=%s", guild_id)
            except Exception as e:
                self.logger.warning("TTS disconnect error: %s", e)

    async def _get_or_join_voice(self, guild_id: int, channel_id: int) -> fluxer.VoiceClient:
        """Get existing voice client or join. Reuses connection if same channel."""
        vc = self._active_voice.get(guild_id)
        if vc and vc.is_connected and vc.channel_id == channel_id:
            return vc
        if vc:
            self._active_voice.pop(guild_id, None)
            try:
                await vc.disconnect()
            except Exception:
                pass
        vc = await self.bot.join_voice(guild_id, channel_id)
        self._active_voice[guild_id] = vc
        return vc

    async def _play_tts(self, guild_id: int, channel_id: int, text: str) -> bool:
        """Generate TTS and play in voice channel. Stays in VC until channel is empty."""
        text = self._clean_text(text)
        if len(text) < 2:
            return False

        cache_key = self._cache_key(text, DEFAULT_VOICE)
        audio_data = await self.cache.get(cache_key)
        if not audio_data:
            audio_data = await self._generate_tts(text, DEFAULT_VOICE)
            if not audio_data:
                return False
            await self.cache.set(cache_key, audio_data)

        try:
            async with self._playing_lock:
                if guild_id in self._playing_guilds:
                    return False  # Already playing, skip
                self._playing_guilds.add(guild_id)

            with tempfile.NamedTemporaryFile(suffix=".mp3", delete=False) as f:
                f.write(audio_data)
                path = f.name

            try:
                vc = await self._get_or_join_voice(guild_id, channel_id)
                await vc.play_file(path, after=lambda e: None)
                # Don't disconnect - stay until channel is empty
                await self._maybe_disconnect_if_alone(guild_id)
            finally:
                Path(path).unlink(missing_ok=True)
        except Exception as e:
            self.logger.error(f"TTS playback error: {e}")
            return False
        finally:
            async with self._playing_lock:
                self._playing_guilds.discard(guild_id)
        return True

    @Cog.listener()
    async def on_message(self, message: fluxer.Message):
        """Auto-TTS: when you type in the TTS channel, bot speaks it in your voice channel."""
        debug = getattr(self.bot.config, "DEBUG_MODE", False)
        if debug:
            self.logger.info("TTS on_message: ch=%s guild=%s author=%s content=%r", message.channel_id, message.guild_id, getattr(message.author, "id", "?"), (message.content or "")[:50])

        if not self.enabled:
            return
        if message.author.bot:
            return
        if not message.content or not message.content.strip():
            if debug:
                self.logger.info("TTS skip: empty content")
            return
        # Skip commands
        prefix = self.bot.command_prefix
        if message.content.startswith(prefix):
            if debug:
                self.logger.info("TTS skip: is command")
            return
        if message.guild_id is None:
            if debug:
                self.logger.info("TTS skip: no guild")
            return
        if self._tts_channel_id is None:
            if debug:
                self.logger.info("TTS skip: no tts channel configured")
            return
        # Only in the designated TTS channel
        msg_ch = int(message.channel_id)
        if msg_ch != self._tts_channel_id:
            if debug:
                self.logger.info("TTS skip: wrong channel (msg_ch=%s, want=%s)", msg_ch, self._tts_channel_id)
            return

        guild_id = int(message.guild_id)
        voice_state = self.bot.get_voice_state(guild_id, message.author.id)
        if not voice_state or not voice_state.channel_id:
            if debug:
                self.logger.info("TTS skip: author not in voice (guild=%s, author=%s)", guild_id, message.author.id)
            return

        if not await self.rate_limiter.check(str(message.author.id)):
            if debug:
                self.logger.info("TTS skip: rate limited")
            return

        self.logger.info("TTS playing for %s: %r", getattr(message.author, "username", "?"), message.content[:60])
        await self._play_tts(guild_id, int(voice_state.channel_id), message.content)

    @Cog.listener()
    async def on_voice_state_update(self, voice_state: fluxer.VoiceState):
        """When anyone leaves a VC, check if we should disconnect (channel now empty)."""
        if not self.enabled:
            return
        if voice_state.guild_id is None:
            return
        guild_id = int(voice_state.guild_id)
        await self._maybe_disconnect_if_alone(guild_id)


async def setup(bot: fluxer.Bot):
    await bot.add_cog(VoiceProcessingCog(bot))
