"""
Secret Santa · FluxerTools

Cozy gift exchanges, native Fluxer style.
Start events, wishlists, anonymous Q&A, history.
"""

from __future__ import annotations

import asyncio
import datetime as dt
import time
from typing import List, Optional

import aiohttp
import fluxer
from fluxer.cog import Cog
from fluxer.models import GuildMember

from . import fluxy
from .secret_santa_assignments import (
    load_history_from_archives,
    make_assignments,
    validate_assignment_possibility,
)
from .secret_santa_storage import (
    ARCHIVE_DIR,
    archive_event,
    get_default_state,
    load_all_archives,
    load_state_with_fallback,
    load_json,
    save_state,
    save_json,
)

DM_DELAY = 1.2
DM_MAX_RETRIES = 4
DM_TIMEOUT = 12
BACKUP_INTERVAL = 3600
ANONYMIZE_RETRY_MAX = 2
ANONYMIZE_TIMEOUT = 15


class SecretSantaCog(Cog):
    """Secret Santa event management – Fluxer port."""

    def __init__(self, bot: fluxer.Bot):
        super().__init__(bot)
        self.logger = bot.logger.getChild("secretsanta")
        self.state = get_default_state()
        self._lock = asyncio.Lock()
        self._backup_task: Optional[asyncio.Task] = None
        self._unloaded = False
        self._executor = getattr(bot, "executor", None)

    def _get_current_event(self) -> Optional[dict]:
        ev = self.state.get("current_event")
        return ev if isinstance(ev, dict) and ev.get("active") else None

    def _get_moderator_role_id(self) -> Optional[int]:
        try:
            rid = getattr(self.bot.config, "FLUXER_MODERATOR_ROLE_ID", None)
            return int(rid) if rid else None
        except (TypeError, ValueError):
            return None

    async def _is_moderator(self, ctx: fluxer.Message) -> bool:
        if not ctx.guild_id:
            return False
        role_id = self._get_moderator_role_id()
        if not role_id:
            return False
        http = getattr(self.bot, "_http", None)
        if not http:
            return False
        try:
            member_data = await http.get_guild_member(ctx.guild_id, ctx.author.id)
            member = GuildMember.from_data(member_data, http, guild_id=ctx.guild_id)
            return member.has_role(role_id)
        except Exception:
            return False

    async def _save_async(self) -> None:
        loop = asyncio.get_running_loop()
        await loop.run_in_executor(
            self._executor,
            lambda: save_state(self.state, logger=self.logger),
        )

    async def _send_dm(self, user_id: int, content: str) -> bool:
        if not content or not str(content).strip():
            return True
        for attempt in range(DM_MAX_RETRIES):
            try:
                user = await asyncio.wait_for(
                    self.bot.fetch_user(str(user_id)),
                    timeout=DM_TIMEOUT,
                )
                await asyncio.wait_for(user.send(content=content), timeout=DM_TIMEOUT)
                return True
            except Exception as e:
                self.logger.warning("DM to %s (attempt %d): %s", user_id, attempt + 1, e)
                if attempt < DM_MAX_RETRIES - 1:
                    await asyncio.sleep(min(2 ** attempt, 5.0))
        return False

    async def _parse_args(self, ctx: fluxer.Message) -> List[str]:
        content = getattr(ctx, "content", "") or ""
        prefix = self.bot.command_prefix if isinstance(self.bot.command_prefix, str) else "!"
        cmd = "ss"
        if prefix and content.startswith(prefix + cmd):
            rest = content[len(prefix) + len(cmd) :].strip()
        else:
            rest = ""
        return rest.split() if rest else []

    async def cog_load(self) -> None:
        loop = asyncio.get_running_loop()
        loaded = await loop.run_in_executor(
            self._executor,
            lambda: load_state_with_fallback(logger=self.logger),
        )
        if isinstance(loaded, dict):
            self.state.clear()
            self.state.update(loaded)
        self._backup_task = asyncio.create_task(self._backup_loop())
        self.logger.info("Secret Santa cog loaded (full features)")

    async def _backup_loop(self) -> None:
        try:
            while not self._unloaded:
                await asyncio.sleep(BACKUP_INTERVAL)
                async with self._lock:
                    await self._save_async()
        except asyncio.CancelledError:
            pass

    async def cog_unload(self) -> None:
        """Fluxer calls await cog_unload() - must be async."""
        if self._unloaded:
            return
        self._unloaded = True
        if self._backup_task:
            self._backup_task.cancel()
            try:
                await self._backup_task
            except asyncio.CancelledError:
                pass
        loop = asyncio.get_running_loop()
        await loop.run_in_executor(
            self._executor,
            lambda: save_state(self.state, logger=self.logger),
        )
        self.logger.info("Secret Santa cog unloaded")

    # ----- Command router -----
    @Cog.command(name="ss")
    async def secret_santa(self, ctx: fluxer.Message):
        """Secret Santa commands. Usage: !ss help"""
        parts = await self._parse_args(ctx)
        sub = (parts[0].lower() if parts else "").strip() or "help"
        rest = parts[1:] if len(parts) > 1 else []

        handlers = {
            "help": self._cmd_help,
            "start": self._cmd_start,
            "join": self._cmd_join,
            "leave": self._cmd_leave,
            "shuffle": self._cmd_shuffle,
            "stop": self._cmd_stop,
            "participants": self._cmd_participants,
            "view_gifts": self._cmd_view_gifts,
            "view_comms": self._cmd_view_comms,
            "ask_giftee": self._cmd_ask_giftee,
            "reply_santa": self._cmd_reply_santa,
            "submit_gift": self._cmd_submit_gift,
            "giftee": self._cmd_giftee,
            "history": self._cmd_history,
            "user_history": self._cmd_user_history,
            "edit_gift": self._cmd_edit_gift,
        }

        if sub == "wishlist" and rest:
            await self._cmd_wishlist(ctx, rest)
            return

        handler = handlers.get(sub)
        if handler:
            await handler(ctx, rest)
        else:
            emb = fluxy.embed_error("Oops", f"Unknown: `{sub}`. Try `!ss help` for options.")
            await ctx.reply(embed=emb)

    async def _cmd_help(self, ctx: fluxer.Message, rest: List[str]) -> None:
        emb = fluxy.embed_cozy(
            "🎄 Secret Santa",
            "Cozy gift exchanges, right here in Fluxer.",
            footer="✨ FluxerTools · !ss help",
        )
        emb.add_field(
            name="Moderators",
            value="`start` · `shuffle` · `stop` · `participants` · `view_gifts` · `view_comms`",
            inline=False,
        )
        emb.add_field(
            name="Participants",
            value="`join` · `leave` · `wishlist add/remove/view/clear` · `giftee` · `ask_giftee` · `reply_santa` · `submit_gift`",
            inline=False,
        )
        emb.add_field(
            name="Anyone",
            value="`history [year]` · `user_history @user` · `edit_gift <year> <desc>`",
            inline=False,
        )
        await ctx.reply(embed=emb)

    async def _cmd_start(self, ctx: fluxer.Message, rest: List[str]) -> None:
        if not ctx.guild_id:
            await ctx.reply(embed=fluxy.embed_error("Oops", "Use this in a server channel."))
            return
        if not await self._is_moderator(ctx):
            await ctx.reply(embed=fluxy.embed_error("Nope", "Moderator role required."))
            return
        event = self._get_current_event()
        if event:
            await ctx.reply(embed=fluxy.embed_error("Already running", "Use `!ss stop` first, then start again."))
            return

        year = dt.date.today().year
        async with self._lock:
            self.state["current_year"] = year
            self.state["current_event"] = {
                "active": True,
                "guild_id": ctx.guild_id,
                "participants": {},
                "assignments": {},
                "wishlists": {},
                "gift_submissions": {},
                "communications": {},
            }
            await self._save_async()
        emb = fluxy.embed_success(f"🎄 Secret Santa {year}", "Event is live! Folks can `!ss join` to sign up.")
        await ctx.reply(embed=emb)

    async def _cmd_join(self, ctx: fluxer.Message, rest: List[str]) -> None:
        if not ctx.guild_id:
            await ctx.reply(embed=fluxy.embed_error("Oops", "Use this in a server channel."))
            return
        event = self._get_current_event()
        if not event:
            await ctx.reply(embed=fluxy.embed_error("No event", "Wait for a mod to run `!ss start`."))
            return
        if str(event.get("guild_id")) != str(ctx.guild_id):
            await ctx.reply(embed=fluxy.embed_error("Wrong server", "This event belongs to another community."))
            return

        uid = str(ctx.author.id)
        name = getattr(ctx.author, "display_name", None) or getattr(ctx.author, "username", f"User {uid}")
        async with self._lock:
            event["participants"][uid] = name
            event["wishlists"][uid] = event.get("wishlists", {}).get(uid) or []
            await self._save_async()
        emb = fluxy.embed_success("🎉 You're in!", f"Secret Santa {self.state['current_year']} — add ideas with `!ss wishlist add <item>`")
        await ctx.reply(embed=emb)

    async def _cmd_leave(self, ctx: fluxer.Message, rest: List[str]) -> None:
        event = self._get_current_event()
        if not event:
            await ctx.reply(embed=fluxy.embed_error("No event", "Nothing active right now."))
            return
        uid = str(ctx.author.id)
        async with self._lock:
            event["participants"].pop(uid, None)
            event["wishlists"].pop(uid, None)
            event["assignments"].pop(uid, None)
            event["gift_submissions"].pop(uid, None)
            event["communications"].pop(uid, None)
            if event.get("assignments"):
                rev = {v: k for k, v in event["assignments"].items()}
                if uid in rev:
                    event["assignments"].pop(rev[uid], None)
            await self._save_async()
        await ctx.reply(embed=fluxy.embed_info("👋 You're out", "Rejoin anytime before the shuffle with `!ss join`."))

    async def _cmd_shuffle(self, ctx: fluxer.Message, rest: List[str]) -> None:
        if not await self._is_moderator(ctx):
            await ctx.reply(embed=fluxy.embed_error("Nope", "Moderator required."))
            return
        event = self._get_current_event()
        if not event:
            await ctx.reply(embed=fluxy.embed_error("No event", "Nothing active."))
            return

        participants = event.get("participants") or {}
        pids = [int(k) for k in participants.keys() if str(k).isdigit()]
        if len(pids) < 2:
            await ctx.reply(embed=fluxy.embed_error("Not enough", "Need 2+ participants. Tell folks to `!ss join`."))
            return

        year = self.state.get("current_year", dt.date.today().year)
        history, _ = load_history_from_archives(ARCHIVE_DIR, exclude_years=[year], logger=self.logger)
        err = validate_assignment_possibility(pids, history)
        if err:
            await ctx.reply(embed=fluxy.embed_error("Can't assign", err))
            return

        try:
            assignments = make_assignments(pids, history, logger=self.logger)
        except ValueError as e:
            await ctx.reply(embed=fluxy.embed_error("Assignment failed", str(e)))
            return

        async with self._lock:
            event["assignments"] = {str(g): r for g, r in assignments.items()}
            await self._save_async()

        failed: List[int] = []
        for giver_id, receiver_id in assignments.items():
            receiver_name = participants.get(str(receiver_id), "Unknown")
            msg = (
                f"🎄 **Secret Santa {year}**\n\n"
                f"Your giftee is **{receiver_name}**!\n\n"
                f"→ `!ss giftee` — see their wishlist\n"
                f"→ `!ss ask_giftee <question>` — ask anonymously"
            )
            if not await self._send_dm(giver_id, msg):
                failed.append(giver_id)
            await asyncio.sleep(DM_DELAY)

        if failed:
            emb = fluxy.embed_success("Assignments sent", f"Couldn't DM: {', '.join(f'<@{u}>' for u in failed)} — they can `!ss giftee` here.")
        else:
            emb = fluxy.embed_success("🎉 All set", "Assignments sent via DM to everyone.")
        await ctx.reply(embed=emb)

    async def _cmd_stop(self, ctx: fluxer.Message, rest: List[str]) -> None:
        if not await self._is_moderator(ctx):
            await ctx.reply(embed=fluxy.embed_error("Nope", "Moderator required."))
            return
        event = self._get_current_event()
        if not event:
            await ctx.reply(embed=fluxy.embed_error("No event", "Nothing to stop."))
            return

        year = self.state.get("current_year", dt.date.today().year)
        try:
            fname = archive_event(event, year, logger=self.logger)
        except Exception as e:
            await ctx.reply(embed=fluxy.embed_error("Archive failed", str(e)))
            return

        async with self._lock:
            self.state["current_event"] = None
            await self._save_async()

        pids = list((event.get("participants") or {}).keys())
        msg = f"✨ Secret Santa {year} wrapped up — thanks for being part of it! 🎄"
        for uid in pids:
            try:
                await self._send_dm(int(uid), msg)
            except Exception:
                pass
            await asyncio.sleep(DM_DELAY)

        await ctx.reply(embed=fluxy.embed_success("🛑 Event ended", f"Archived to `{fname}`. Thanks, everyone!"))

    async def _cmd_participants(self, ctx: fluxer.Message, rest: List[str]) -> None:
        if not await self._is_moderator(ctx):
            await ctx.reply(embed=fluxy.embed_error("Nope", "Moderator required."))
            return
        event = self._get_current_event()
        if not event:
            await ctx.reply(embed=fluxy.embed_error("No event", "Nothing active."))
            return
        parts = list((event.get("participants") or {}).items())
        if not parts:
            await ctx.reply(embed=fluxy.embed_info("No one yet", "Tell folks to `!ss join`."))
            return
        lines = [f"• {name} <@{uid}>" for uid, name in parts[:25]]
        emb = fluxy.embed_cozy("Participants", "\n".join(lines))
        await ctx.reply(embed=emb)

    async def _cmd_view_gifts(self, ctx: fluxer.Message, rest: List[str]) -> None:
        if not await self._is_moderator(ctx):
            await ctx.reply(embed=fluxy.embed_error("Nope", "Moderator required."))
            return
        event = self._get_current_event()
        if not event:
            await ctx.reply(embed=fluxy.embed_error("No event", "Nothing active."))
            return
        gifts = event.get("gift_submissions") or {}
        if not gifts:
            await ctx.reply(embed=fluxy.embed_info("Nothing yet", "No gifts submitted."))
            return
        lines = []
        participants = event.get("participants") or {}
        for giver_id, data in gifts.items():
            if isinstance(data, dict):
                desc = data.get("gift", data)
                rec = data.get("receiver_name", "?")
            else:
                desc, rec = data, "?"
            giver = participants.get(str(giver_id), f"User {giver_id}")
            lines.append(f"• **{giver}** → {rec}: {fluxy.truncate(desc, 100)}")
        emb = fluxy.embed_cozy("Gift submissions", "\n".join(lines[:15]))
        await ctx.reply(embed=emb)

    async def _cmd_view_comms(self, ctx: fluxer.Message, rest: List[str]) -> None:
        if not await self._is_moderator(ctx):
            await ctx.reply(embed=fluxy.embed_error("Nope", "Moderator required."))
            return
        event = self._get_current_event()
        if not event:
            await ctx.reply(embed=fluxy.embed_error("No event", "Nothing active."))
            return
        comms = event.get("communications") or {}
        if not comms:
            await ctx.reply(embed=fluxy.embed_info("Nothing yet", "No anonymous Q&A yet."))
            return
        lines = []
        participants = event.get("participants") or {}
        for santa_id, entry in list(comms.items())[:10]:
            if isinstance(entry, dict):
                thread = entry.get("thread", [])
                giftee = entry.get("giftee_id", "?")
            else:
                thread, giftee = [], "?"
            santa = participants.get(str(santa_id), f"Santa {santa_id}")
            for t in thread[:3]:
                m = t.get("message") or t.get("rewritten") or ""
                lines.append(f"• {santa} ↔ {giftee}: {fluxy.truncate(m, 60)}")
        body = "\n".join(lines[:20]) if lines else "No messages."
        emb = fluxy.embed_cozy("Anonymous Q&A", body)
        await ctx.reply(embed=emb)

    async def _cmd_ask_giftee(self, ctx: fluxer.Message, rest: List[str]) -> None:
        question = " ".join(rest).strip() if rest else ""
        if not question:
            await ctx.reply(embed=fluxy.embed_info("Usage", "`!ss ask_giftee <your question>`"))
            return
        event = self._get_current_event()
        if not event:
            await ctx.reply(embed=fluxy.embed_error("No event", "Nothing active."))
            return
        uid = str(ctx.author.id)
        assignments = event.get("assignments") or {}
        receiver_id = assignments.get(uid)
        if not receiver_id:
            await ctx.reply(embed=fluxy.embed_error("Not yet", "Wait for the organizer to run `!ss shuffle`."))
            return
        receiver_id = str(receiver_id)
        santa_id = str(ctx.author.id)
        rewritten = await self._anonymize(question, "question")
        q_text = rewritten or question
        msg = (
            f"❓ **Secret Santa – Your Santa asks:**\n\n"
            f"*\"{q_text}\"\*\n\n"
            f"Reply with `!ss reply_santa <your answer>`"
        )
        ok = await self._send_dm(int(receiver_id), msg)
        if ok:
            async with self._lock:
                comms = event.get("communications") or {}
                entry = comms.get(santa_id)
                if not isinstance(entry, dict):
                    entry = {"giftee_id": receiver_id, "thread": []}
                    comms[santa_id] = entry
                entry.setdefault("thread", []).append({
                    "type": "question",
                    "message": question,
                    "rewritten": rewritten,
                    "timestamp": time.time(),
                })
                event["communications"] = comms
                await self._save_async()
            await ctx.reply(embed=fluxy.embed_success("Sent!", "Your question went to your giftee anonymously."))
        else:
            await ctx.reply(embed=fluxy.embed_error("Can't DM", "Your giftee may have DMs off."))

    async def _cmd_reply_santa(self, ctx: fluxer.Message, rest: List[str]) -> None:
        reply = " ".join(rest).strip() if rest else ""
        if not reply:
            await ctx.reply(embed=fluxy.embed_info("Usage", "`!ss reply_santa <your reply>`"))
            return
        event = self._get_current_event()
        if not event:
            await ctx.reply(embed=fluxy.embed_error("No event", "Nothing active."))
            return
        giftee_id = str(ctx.author.id)
        assignments = event.get("assignments") or {}
        santa_id = None
        for s, r in assignments.items():
            if str(r) == giftee_id:
                santa_id = s
                break
        if not santa_id:
            await ctx.reply(embed=fluxy.embed_error("No question yet", "Your Santa hasn't asked anything."))
            return
        rewritten = await self._anonymize(reply, "reply")
        r_text = rewritten or reply
        msg = (
            f"💌 **Secret Santa – Your giftee replies:**\n\n"
            f"*\"{r_text}\"\*\n\n"
            f"Ask another question: `!ss ask_giftee <question>`"
        )
        ok = await self._send_dm(int(santa_id), msg)
        if ok:
            async with self._lock:
                comms = event.get("communications") or {}
                entry = comms.get(santa_id)
                if not isinstance(entry, dict):
                    entry = {"giftee_id": giftee_id, "thread": []}
                    comms[santa_id] = entry
                entry.setdefault("thread", []).append({
                    "type": "reply",
                    "message": reply,
                    "rewritten": rewritten,
                    "timestamp": time.time(),
                })
                event["communications"] = comms
                await self._save_async()
            await ctx.reply(embed=fluxy.embed_success("Sent!", "Your Santa got your reply."))
        else:
            await ctx.reply(embed=fluxy.embed_error("Can't DM", "Couldn't reach your Santa."))

    async def _anonymize(self, text: str, msg_type: str) -> str:
        text = (text or "").replace("\x00", " ").strip()[:2000]
        if not text:
            return ""
        key = getattr(self.bot.config, "OPENAI_API_KEY", None)
        if not key:
            return ""
        payload = {
            "model": "gpt-3.5-turbo",
            "messages": [{
                "role": "user",
                "content": (
                    f"Rewrite this Secret Santa {msg_type} with minimal changes - "
                    "just enough to obscure writing style. Keep 80-90% of original. Same meaning.\n\n"
                    f"Original: {text}\n\nRewritten:"
                ),
            }],
            "max_tokens": 150,
            "temperature": 0.2,
        }
        for attempt in range(ANONYMIZE_RETRY_MAX):
            try:
                session = await self.bot.http_mgr.get_session(timeout=ANONYMIZE_TIMEOUT)
                async with session.post(
                    "https://api.openai.com/v1/chat/completions",
                    json=payload,
                    headers={"Authorization": f"Bearer {key}", "Content-Type": "application/json"},
                    timeout=aiohttp.ClientTimeout(total=ANONYMIZE_TIMEOUT),
                ) as resp:
                    if resp.status == 200:
                        data = await resp.json()
                        out = (data.get("choices") or [{}])[0].get("message", {}).get("content", "").strip()
                        return out.replace("Rewritten:", "").strip() or text
            except Exception as e:
                self.logger.debug("Anonymize attempt %d: %s", attempt + 1, e)
                if attempt < ANONYMIZE_RETRY_MAX - 1:
                    await asyncio.sleep(1.0 * (2 ** attempt))
        return ""

    async def _cmd_submit_gift(self, ctx: fluxer.Message, rest: List[str]) -> None:
        desc = " ".join(rest).strip() if rest else ""
        if not desc:
            await ctx.reply(embed=fluxy.embed_info("Usage", "`!ss submit_gift <what you gave>`"))
            return
        event = self._get_current_event()
        if not event:
            await ctx.reply(embed=fluxy.embed_error("No event", "Nothing active."))
            return
        uid = str(ctx.author.id)
        assignments = event.get("assignments") or {}
        receiver_id = assignments.get(uid)
        if not receiver_id:
            await ctx.reply(embed=fluxy.embed_error("Not yet", "You don't have a giftee yet."))
            return
        participants = event.get("participants") or {}
        rec_name = participants.get(str(receiver_id), "Unknown")
        async with self._lock:
            event.setdefault("gift_submissions", {})[uid] = {
                "gift": desc,
                "receiver_id": str(receiver_id),
                "receiver_name": rec_name,
            }
            await self._save_async()
        await ctx.reply(embed=fluxy.embed_success("Gift recorded!", f"For {rec_name}."))

    async def _cmd_wishlist(self, ctx: fluxer.Message, rest: List[str]) -> None:
        if not rest:
            await ctx.reply(embed=fluxy.embed_info("Wishlist", "`add <item>` · `remove <n>` · `view` · `clear`"))
            return
        sub = rest[0].lower()
        event = self._get_current_event()
        if not event:
            await ctx.reply(embed=fluxy.embed_error("No event", "Nothing active."))
            return
        uid = str(ctx.author.id)
        if uid not in (event.get("participants") or {}):
            await ctx.reply(embed=fluxy.embed_error("Not signed up", "Use `!ss join` first."))
            return
        wishlists = event.setdefault("wishlists", {})
        lst = wishlists.get(uid)
        if not isinstance(lst, list):
            lst = []
            wishlists[uid] = lst

        if sub == "add":
            item = " ".join(rest[1:]).strip()
            if not item:
                await ctx.reply(embed=fluxy.embed_info("Usage", "`!ss wishlist add <item>`"))
                return
            lst.append(item)
            await self._save_async()
            await ctx.reply(embed=fluxy.embed_success("Added", fluxy.truncate(item, 80)))
        elif sub == "remove":
            try:
                n = int(rest[1]) if len(rest) > 1 else 0
            except ValueError:
                await ctx.reply(embed=fluxy.embed_info("Usage", "`!ss wishlist remove <number>`"))
                return
            if 1 <= n <= len(lst):
                lst.pop(n - 1)
                await self._save_async()
                await ctx.reply(embed=fluxy.embed_success("Removed", ""))
            else:
                await ctx.reply(embed=fluxy.embed_error("Invalid", f"Use 1–{len(lst)}."))
        elif sub == "view":
            if not lst:
                await ctx.reply(embed=fluxy.embed_info("Empty", "Use `!ss wishlist add <item>`"))
                return
            lines = [f"{i+1}. {fluxy.truncate(x, 60)}" for i, x in enumerate(lst[:15])]
            emb = fluxy.embed_cozy("Your wishlist", "\n".join(lines))
            await ctx.reply(embed=emb)
        elif sub == "clear":
            wishlists[uid] = []
            await self._save_async()
            await ctx.reply(embed=fluxy.embed_success("Cleared", "Wishlist is empty."))
        else:
            await ctx.reply(embed=fluxy.embed_info("Wishlist", "`add` · `remove` · `view` · `clear`"))

    async def _cmd_giftee(self, ctx: fluxer.Message, rest: List[str]) -> None:
        event = self._get_current_event()
        if not event:
            await ctx.reply(embed=fluxy.embed_error("No event", "Nothing active."))
            return
        uid = str(ctx.author.id)
        assignments = event.get("assignments") or {}
        receiver_id = assignments.get(uid)
        if not receiver_id:
            await ctx.reply(embed=fluxy.embed_error("Not yet", "Wait for `!ss shuffle`."))
            return
        receiver_id = str(receiver_id)
        participants = event.get("participants") or {}
        rec_name = participants.get(receiver_id, "Unknown")
        wishlists = event.get("wishlists") or {}
        items = wishlists.get(receiver_id)
        if not isinstance(items, list):
            items = []
        if not items:
            emb = fluxy.embed_cozy(f"Your giftee: {rec_name}", "No wishlist yet. They might add one soon!")
        else:
            lines = [f"• {fluxy.truncate(x, 80)}" for x in items[:15]]
            emb = fluxy.embed_cozy(f"Your giftee: {rec_name}", "**Wishlist:**\n" + "\n".join(lines))
        await ctx.reply(embed=emb)

    async def _cmd_history(self, ctx: fluxer.Message, rest: List[str]) -> None:
        archives = load_all_archives(logger=self.logger)
        if not archives:
            await ctx.reply(embed=fluxy.embed_info("No archives", "No past events yet."))
            return
        years = sorted(archives.keys(), reverse=True)
        if rest:
            try:
                y = int(rest[0])
                if y in archives:
                    ev = archives[y].get("event") or {}
                    parts = ev.get("participants") or {}
                    gifts = ev.get("gift_submissions") or {}
                    emb = fluxy.embed_cozy(
                        f"Secret Santa {y}",
                        f"Participants: {len(parts)} · Gifts: {len(gifts)}",
                    )
                    await ctx.reply(embed=emb)
                    return
            except ValueError:
                pass
        emb = fluxy.embed_cozy("Archives", ", ".join(str(y) for y in years[:15]))
        await ctx.reply(embed=emb)

    async def _cmd_user_history(self, ctx: fluxer.Message, rest: List[str]) -> None:
        if not rest:
            await ctx.reply(embed=fluxy.embed_info("Usage", "`!ss user_history @user` or `<user_id>`"))
            return
        target = rest[0].strip()
        uid = None
        if target.isdigit():
            uid = target
        elif getattr(ctx, "mentions", None):
            uid = str(ctx.mentions[0].id)
        if not uid:
            await ctx.reply(embed=fluxy.embed_error("Who?", "Use @mention or user ID."))
            return
        archives = load_all_archives(logger=self.logger)
        years: List[int] = []
        for y, data in archives.items():
            ev = data.get("event") or {}
            parts = ev.get("participants") or {}
            if uid in parts:
                years.append(y)
        if not years:
            await ctx.reply(embed=fluxy.embed_info("No history", "That user hasn't participated yet."))
            return
        emb = fluxy.embed_cozy("Participation", ", ".join(str(y) for y in sorted(years, reverse=True)))
        await ctx.reply(embed=emb)

    async def _cmd_edit_gift(self, ctx: fluxer.Message, rest: List[str]) -> None:
        if len(rest) < 2:
            await ctx.reply(embed=fluxy.embed_info("Usage", "`!ss edit_gift <year> <new description>`"))
            return
        try:
            year = int(rest[0])
        except ValueError:
            await ctx.reply(embed=fluxy.embed_error("Invalid year", "Use a number like 2024."))
            return
        desc = " ".join(rest[1:]).strip()
        if not desc:
            await ctx.reply(embed=fluxy.embed_error("Missing", "Provide a description."))
            return
        path = ARCHIVE_DIR / f"{year}.json"
        if not path.exists():
            await ctx.reply(embed=fluxy.embed_error("No archive", f"Nothing for {year}."))
            return
        data = load_json(path, {})
        ev = data.get("event") or {}
        participants = ev.get("participants") or {}
        gifts = ev.get("gift_submissions") or {}
        uid = str(ctx.author.id)
        if uid not in participants:
            await ctx.reply(embed=fluxy.embed_error("Not a participant", f"You weren't in {year}."))
            return
        assignments = ev.get("assignments") or {}
        receiver_id = assignments.get(uid)
        rec_name = participants.get(str(receiver_id), "Unknown") if receiver_id else "Unknown"
        gifts[uid] = {"gift": desc, "receiver_id": str(receiver_id), "receiver_name": rec_name}
        data["event"] = ev
        ev["gift_submissions"] = gifts
        try:
            save_json(path, data, self.logger)
        except Exception as e:
            await ctx.reply(embed=fluxy.embed_error("Save failed", str(e)))
            return
        await ctx.reply(embed=fluxy.embed_success("Updated!", f"Gift for {year} edited."))


async def setup(bot: fluxer.Bot):
    await bot.add_cog(SecretSantaCog(bot))
