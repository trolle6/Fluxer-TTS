# Fluxer API Capabilities (Reference)

Research notes for FluxerTools development. Fluxer is Discord-like with similar concepts.

## Gateway Events (Real-time)

| Event | Scope | Use |
|-------|-------|-----|
| READY | session | Initial state |
| GUILD_CREATE | guild | Guild + channels + **voice_states** |
| MESSAGE_CREATE | channel | New messages |
| MESSAGE_UPDATE | channel | Edits |
| VOICE_STATE_UPDATE | guild | Join/leave/move voice |
| VOICE_SERVER_UPDATE | presence | Voice server info |

## HTTP API (fluxer.py)

- **Channels:** send_message, edit_message, get_channel, get_messages
- **Guilds:** get_guild, get_guild_member, get_guild_roles, list_guild_channels
- **Voice:** Via gateway (join_voice → VoiceClient with LiveKit)

## fluxer.py Models

- `Message` – reply(), send(), edit()
- `Channel` – send(), fetch
- `Embed` – title, description, fields, image, to_dict()
- `VoiceState` – user_id, channel_id, guild_id, member (GuildMember with roles)
- `GuildMember` – has_role(role_id), display_name

## Checks (fluxer.checks)

- `@has_role(name="Mod" | id=123)` – Require role
- `@has_permission(Permissions.KICK_MEMBERS)` – Require permission

## Voice

- LiveKit-based (not Discord voice)
- `bot.join_voice(guild_id, channel_id)` → VoiceClient
- `FFmpegPCMAudio` for file playback
- Voice states: from VOICE_STATE_UPDATE; GUILD_CREATE also has voice_states (we populate this)

## Limits

- One connection per token (parallel sessions get Invalid session)
- Rate limits per endpoint ( Fluxer docs )
