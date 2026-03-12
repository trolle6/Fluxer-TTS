"""
DALL-E Image Generation Cog - Fluxer Port
Ported from WaveTechToolBoxx

COMMANDS:
- !image <prompt> [size] [quality] - Generate AI image
  size: 1024x1024, 1792x1024, 1024x1792
  quality: standard, hd
"""

import asyncio
import hashlib
import re
import time
from dataclasses import dataclass
from typing import Dict, Optional

import aiohttp
import fluxer
from fluxer.cog import Cog

from . import utils

SIZES = ("1024x1024", "1792x1024", "1024x1792")
QUALITIES = ("standard", "hd")


@dataclass
class GenerationJob:
    user_id: int
    prompt: str
    size: str
    quality: str
    status_message: "fluxer.Message"
    timestamp: float

    DEFAULT_EXPIRY_SECONDS = 300

    def is_expired(self, max_age: int = DEFAULT_EXPIRY_SECONDS) -> bool:
        return (time.time() - self.timestamp) > max_age


class DALLECog(Cog):
    """DALL-E 3 image generation with queue and cache."""

    def __init__(self, bot: fluxer.Bot):
        super().__init__(bot)
        self.logger = bot.logger.getChild("dalle")

        if not getattr(bot.config, "OPENAI_API_KEY", None):
            self.logger.warning("OPENAI_API_KEY not configured - DALL-E disabled")
            self.enabled = False
            return

        self.enabled = True
        rate_limit = bot.config.RATE_LIMIT_REQUESTS
        rate_window = bot.config.RATE_LIMIT_WINDOW
        max_queue = bot.config.MAX_QUEUE_SIZE

        self.rate_limiter = utils.RateLimiter(limit=rate_limit, window=rate_window)
        self.cache = utils.LRUCache[str](max_size=max_queue, ttl=3600)
        self.queue: asyncio.Queue = asyncio.Queue(maxsize=max_queue)
        self.processor_task: Optional[asyncio.Task] = None
        self.is_processing = False

        self.api_url = "https://api.openai.com/v1/images/generations"
        self.max_retries = 3
        self.stats = {"total_requests": 0, "successful": 0, "failed": 0, "cache_hits": 0, "total_time": 0.0}
        self._stats_lock = asyncio.Lock()
        self._shutdown = asyncio.Event()
        self._unloaded = False
        self._health_check_task: Optional[asyncio.Task] = None

        self.logger.info("DALL-E cog initialized")

    def _create_error_embed(self, error_msg: str, elapsed: float = 0.0) -> fluxer.Embed:
        embed = fluxer.Embed(title="❌ Generation Failed", description=error_msg, color=0xE74C3C)
        if elapsed > 0:
            embed.set_footer(text=f"Time: {elapsed:.1f}s")
        return embed

    def _create_success_embed(self, image_url: str, prompt: str, quality: str, elapsed: float) -> fluxer.Embed:
        preview = prompt[:200] + "..." if len(prompt) > 200 else prompt
        embed = fluxer.Embed(title="🎨 Image Generated!", description=f"**Prompt:** {preview}", color=0x2ECC71)
        embed.set_image(url=image_url)
        embed.add_field(name="Time", value=f"{elapsed:.1f}s", inline=True)
        embed.add_field(name="Model", value="DALL-E 3", inline=True)
        embed.add_field(name="Quality", value=quality.upper(), inline=True)
        embed.set_footer(text="💡 Tip: Use specific details for better results!")
        return embed

    def _create_loading_embed(self, prompt: str, size: str, quality: str) -> fluxer.Embed:
        embed = fluxer.Embed(
            title="🎨 Generating Image",
            description="Creating your masterpiece with DALL-E 3...",
            color=0x3498DB,
        )
        preview = prompt[:100] + "..." if len(prompt) > 100 else prompt
        embed.add_field(name="Prompt", value=f"```{preview}```", inline=False)
        embed.add_field(name="Quality", value=quality.upper(), inline=True)
        embed.add_field(name="Size", value=size, inline=True)
        embed.set_footer(text="This may take 15-30 seconds")
        return embed

    def _create_cache_embed(self, image_url: str) -> fluxer.Embed:
        embed = fluxer.Embed(title="🎨 Image Generated!", description="Retrieved from cache", color=0x3498DB)
        embed.set_image(url=image_url)
        embed.set_footer(text="⚡ Retrieved from cache")
        return embed

    def _create_queue_embed(self, queue_size: int) -> fluxer.Embed:
        embed = fluxer.Embed(
            title="⏳ Image Generation Queued",
            description="Your request has been added to the queue",
            color=0x3498DB,
        )
        embed.add_field(name="Position", value=f"#{queue_size}", inline=True)
        embed.add_field(name="Est. Wait", value=f"~{queue_size * 30}s", inline=True)
        embed.set_footer(text="You'll be notified when it's ready")
        return embed

    def _cache_key(self, prompt: str, size: str, quality: str) -> str:
        return hashlib.sha256(f"{prompt}:{size}:{quality}".encode()).hexdigest()

    def _get_openai_headers(self) -> Dict[str, str]:
        return {
            "Authorization": f"Bearer {self.bot.config.OPENAI_API_KEY}",
            "Content-Type": "application/json",
        }

    async def _generate_image(self, prompt: str, size: str = "1024x1024", quality: str = "hd") -> Dict:
        headers = self._get_openai_headers()
        payload = {
            "model": "dall-e-3",
            "prompt": prompt,
            "n": 1,
            "size": size,
            "quality": quality,
            "response_format": "url",
            "style": "vivid",
        }
        async with self._stats_lock:
            self.stats["total_requests"] += 1

        for attempt in range(self.max_retries):
            try:
                session = await self.bot.http_mgr.get_session()
                timeout = aiohttp.ClientTimeout(total=45)
                async with session.post(self.api_url, json=payload, headers=headers, timeout=timeout) as resp:
                    if resp.status == 200:
                        result = await resp.json()
                        async with self._stats_lock:
                            self.stats["successful"] += 1
                        return {"success": True, "data": result}
                    elif resp.status == 429:
                        retry_after = int(resp.headers.get("Retry-After", "60"))
                        if attempt < self.max_retries - 1:
                            await asyncio.sleep(min(retry_after, 30))
                            continue
                        async with self._stats_lock:
                            self.stats["failed"] += 1
                        return {"success": False, "error": f"Rate limited. Try again in {retry_after}s"}
                    elif resp.status == 400:
                        try:
                            err_data = await resp.json()
                            err_obj = err_data.get("error", {})
                            err_msg = err_obj.get("message", "Bad request") if isinstance(err_obj, dict) else "Bad request"
                        except Exception:
                            err_msg = "Bad request"
                        if "content_policy" in err_msg.lower():
                            async with self._stats_lock:
                                self.stats["failed"] += 1
                            return {"success": False, "error": "🚫 Content policy violation"}
                        async with self._stats_lock:
                            self.stats["failed"] += 1
                        return {"success": False, "error": f"Invalid request: {err_msg}"}
                    elif resp.status == 401:
                        async with self._stats_lock:
                            self.stats["failed"] += 1
                        return {"success": False, "error": "🔒 API authentication failed"}
                    else:
                        if attempt < self.max_retries - 1:
                            await asyncio.sleep(2**attempt)
                            continue
                        async with self._stats_lock:
                            self.stats["failed"] += 1
                        return {"success": False, "error": f"API error {resp.status}"}
            except asyncio.TimeoutError:
                if attempt < self.max_retries - 1:
                    await asyncio.sleep(2**attempt)
                    continue
                async with self._stats_lock:
                    self.stats["failed"] += 1
                return {"success": False, "error": "⏰ Request timeout"}
            except Exception as e:
                self.logger.error(f"Generation error: {e}")
                if attempt < self.max_retries - 1:
                    await asyncio.sleep(2**attempt)
                    continue
                async with self._stats_lock:
                    self.stats["failed"] += 1
                return {"success": False, "error": str(e)[:50]}

        async with self._stats_lock:
            self.stats["failed"] += 1
        return {"success": False, "error": "Max retries exceeded"}

    def _extract_image_url(self, result: Dict) -> Optional[str]:
        try:
            api_resp = result.get("data")
            if not isinstance(api_resp, dict):
                return None
            images = api_resp.get("data")
            if not isinstance(images, list) or not images:
                return None
            first = images[0]
            if not isinstance(first, dict):
                return None
            url = first.get("url")
            return str(url) if url else None
        except (KeyError, TypeError):
            return None

    async def _process_queue(self):
        try:
            while not self._shutdown.is_set():
                try:
                    job = await asyncio.wait_for(self.queue.get(), timeout=60)
                    if self._shutdown.is_set():
                        break
                    if job.is_expired():
                        try:
                            await job.status_message.edit(content="⏰ Request expired")
                        except Exception:
                            pass
                        continue

                    self.is_processing = True
                    try:
                        loading_embed = self._create_loading_embed(job.prompt, job.size, job.quality)
                        await job.status_message.edit(embeds=[loading_embed.to_dict()])
                    except Exception:
                        pass

                    start = time.time()
                    result = await self._generate_image(job.prompt, job.size, job.quality)
                    elapsed = time.time() - start
                    async with self._stats_lock:
                        self.stats["total_time"] += elapsed

                    image_url = self._extract_image_url(result) if result.get("success") else None
                    if image_url:
                        await self.cache.set(self._cache_key(job.prompt, job.size, job.quality), image_url)

                    if not result.get("success"):
                        embed = self._create_error_embed(result.get("error", "Unknown error"), elapsed)
                        await job.status_message.edit(embeds=[embed.to_dict()])
                    elif image_url:
                        embed = self._create_success_embed(image_url, job.prompt, job.quality, elapsed)
                        await job.status_message.edit(embeds=[embed.to_dict()])
                    else:
                        embed = self._create_error_embed("Invalid API response format", elapsed)
                        await job.status_message.edit(embeds=[embed.to_dict()])
                except asyncio.TimeoutError:
                    continue
                except asyncio.CancelledError:
                    raise
                except Exception as e:
                    self.logger.error(f"Queue error: {e}")
                    try:
                        await job.status_message.edit(content="❌ An error occurred")
                    except Exception:
                        pass
                finally:
                    self.is_processing = False
        except asyncio.CancelledError:
            pass

    async def _health_check_loop(self):
        try:
            while not self._shutdown.is_set():
                await asyncio.sleep(600)
                if self._shutdown.is_set():
                    break
                if self.processor_task and self.processor_task.done():
                    if not self.queue.empty() and not self.is_processing:
                        self.processor_task = asyncio.create_task(self._process_queue())
                elif self.queue.qsize() > 0 and not self.is_processing and (not self.processor_task or self.processor_task.done()):
                    self.processor_task = asyncio.create_task(self._process_queue())
        except asyncio.CancelledError:
            pass

    def _parse_prompt_args(self, prompt: str) -> tuple[str, str, str]:
        """Parse optional size and quality from end of prompt."""
        size, quality = "1024x1024", "hd"
        parts = prompt.strip().split()
        while parts:
            last = parts[-1].lower()
            if last in QUALITIES:
                quality = last
                parts.pop()
            elif last in SIZES:
                size = last
                parts.pop()
            else:
                break
        return " ".join(parts).strip() or "a beautiful image", size, quality

    @Cog.command(name="image")
    async def image(self, ctx: fluxer.Message, *, prompt: str):
        """Generate an image with DALL-E 3. Usage: !image <prompt> [size] [quality]"""
        if not self.enabled:
            await ctx.reply("❌ DALL-E is not configured")
            return

        prompt, size, quality = self._parse_prompt_args(prompt)
        if len(prompt) < 3:
            await ctx.reply("❌ Prompt too short (min 3 characters)")
            return

        if not await self.rate_limiter.check(str(ctx.author.id)):
            await ctx.reply("⏳ Rate limited. Please wait before generating another image.")
            return

        cache_key = self._cache_key(prompt, size, quality)
        cached = await self.cache.get(cache_key)
        if cached:
            async with self._stats_lock:
                self.stats["cache_hits"] += 1
            embed = self._create_cache_embed(cached)
            await ctx.reply(embed=embed)
            return

        queue_embed = self._create_queue_embed(1)
        status_msg = await ctx.reply(embed=queue_embed)

        job = GenerationJob(
            user_id=ctx.author.id,
            prompt=prompt,
            size=size,
            quality=quality,
            status_message=status_msg,
            timestamp=time.time(),
        )
        try:
            self.queue.put_nowait(job)
            queue_embed = self._create_queue_embed(self.queue.qsize())
            await status_msg.edit(embeds=[queue_embed.to_dict()])
        except asyncio.QueueFull:
            await status_msg.edit(content="❌ Queue is full. Try again in a few minutes.")

    async def daily_maintenance(self):
        if self.enabled and hasattr(self, "cache"):
            await self.cache.cleanup()

    async def cog_load(self):
        if not self.enabled:
            return
        self.processor_task = asyncio.create_task(self._process_queue())
        self._health_check_task = asyncio.create_task(self._health_check_loop())
        self.logger.info("DALL-E cog loaded")

    async def cog_unload(self):
        if not self.enabled or self._unloaded:
            return
        self._unloaded = True
        self._shutdown.set()
        if self.processor_task:
            self.processor_task.cancel()
            try:
                await self.processor_task
            except asyncio.CancelledError:
                pass
        if self._health_check_task:
            self._health_check_task.cancel()
            try:
                await self._health_check_task
            except asyncio.CancelledError:
                pass
        self.logger.info("DALL-E cog unloaded")


async def setup(bot: fluxer.Bot):
    await bot.add_cog(DALLECog(bot))
