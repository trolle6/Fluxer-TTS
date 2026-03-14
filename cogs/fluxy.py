"""
Fluxy – Fluxer-native styling

Consistent embeds, colors, truncation. Brand: #4641D9.
"""

from __future__ import annotations

from typing import Optional

import fluxer

# Fluxer brand & palette
FLUXER_BLUE = 0x4641D9      # Primary – from fluxer.app/press
FLUXER_SUCCESS = 0x2ECC71    # Green – success, confirmations
FLUXER_ERROR = 0xE74C3C     # Soft red – errors, oops
FLUXER_COZY = 0xC0392B       # Mulled wine – holiday, warm
FLUXER_SNOW = 0x3498DB      # Light blue – info, neutral
FLUXER_AMBER = 0xF39C12     # Warm – tips, highlights

FOOTER = "✨ FluxerTools"


def truncate(s: Optional[str], max_len: int = 80, suffix: str = "...") -> str:
    """Safe truncate with ellipsis."""
    if s is None or not isinstance(s, str):
        return ""
    s = s.strip()
    if len(s) <= max_len:
        return s
    return s[: max_len - len(suffix)].rstrip() + suffix


def embed(
    title: str,
    description: str = "",
    *,
    color: int = FLUXER_BLUE,
    footer: Optional[str] = FOOTER,
    **fields: str | tuple,
) -> fluxer.Embed:
    """Build a Fluxy embed with consistent styling."""
    e = fluxer.Embed(title=title, description=description or None, color=color)
    for name, value in fields.items():
        if name == "footer":
            e.set_footer(text=str(value))
        elif isinstance(value, tuple):
            e.add_field(name=name, value=str(value[0]), inline=bool(value[1]) if len(value) > 1 else False)
        else:
            e.add_field(name=name, value=str(value), inline=False)
    if footer and "footer" not in fields:
        e.set_footer(text=footer)
    return e


def embed_success(title: str, description: str = "", **fields) -> fluxer.Embed:
    """Success / confirmation embed."""
    return embed(title, description, color=FLUXER_SUCCESS, **fields)


def embed_error(title: str, description: str = "", **fields) -> fluxer.Embed:
    """Error / oops embed."""
    return embed(title, description, color=FLUXER_ERROR, **fields)


def embed_info(title: str, description: str = "", **fields) -> fluxer.Embed:
    """Neutral info embed."""
    return embed(title, description, color=FLUXER_SNOW, **fields)


def embed_cozy(title: str, description: str = "", **fields) -> fluxer.Embed:
    """Holiday / warm embed for Secret Santa."""
    return embed(title, description, color=FLUXER_COZY, **fields)
