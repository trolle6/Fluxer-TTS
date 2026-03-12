"""
Secret Santa Cog - Fluxer Port (Stub)
Ported from WaveTechToolBoxx - simplified for initial release

The full Secret Santa feature (event creation, signups, assignments, etc.)
requires extensive adaptation from Discord slash commands to Fluxer prefix commands.
This stub provides basic !ss help and structure for future expansion.

Original: https://github.com/trolle6/WaveTechToolBoxx
"""

import fluxer
from fluxer.cog import Cog


class SecretSantaCog(Cog):
    """Secret Santa event management - stub for Fluxer."""

    def __init__(self, bot: fluxer.Bot):
        super().__init__(bot)
        self.logger = bot.logger.getChild("secretsanta")
        self.logger.info("Secret Santa cog loaded (stub - use Discord version for full features)")

    @Cog.command(name="ss")
    async def secret_santa(self, ctx: fluxer.Message):
        """Secret Santa commands. Usage: !ss help"""
        # Parse args from message content (fluxer passes ctx=message for Cog commands)
        content = getattr(ctx, "content", "") or ""
        prefix = self.bot.command_prefix
        rest = content[len(prefix) + len("ss") :].strip() if content.startswith(prefix + "ss") else ""

        if not rest or rest.lower() == "help":
            help_text = """
**🎄 FluxerTools Secret Santa (Stub)**

This is a simplified port. Full Secret Santa features are available in the
original [WaveTechToolBoxx](https://github.com/trolle6/WaveTechToolBoxx) for Discord.

**Available commands:**
- `!ss help` - Show this message

**Planned:** Event creation, signups, assignments, gift exchanges.
To contribute or request features, open an issue on the FluxerTools repo.
"""
            await ctx.reply(help_text.strip())
            return
        await ctx.reply("Unknown subcommand. Use `!ss help`")


async def setup(bot: fluxer.Bot):
    await bot.add_cog(SecretSantaCog(bot))
