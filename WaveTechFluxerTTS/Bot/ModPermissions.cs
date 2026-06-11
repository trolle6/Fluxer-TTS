using System.Text.Json;
using WaveTechFluxerTTS.Fluxer;

namespace WaveTechFluxerTTS.Bot;

public static class ModPermissions
{
    public static bool IsModerator(InteractionContext ctx, BotConfig config)
    {
        if (ctx.Member is null)
            return false;
        if (ctx.Member.Value.TryGetProperty("permissions", out var perms))
        {
            if (perms.ValueKind == JsonValueKind.String &&
                ulong.TryParse(perms.GetString(), out var p) &&
                (p & 8) == 8)
                return true;
            if (perms.ValueKind == JsonValueKind.Number && (perms.GetUInt64() & 8) == 8)
                return true;
        }
        if (config.ModeratorRoleId is null)
            return false;
        if (!ctx.Member.Value.TryGetProperty("roles", out var roles))
            return false;
        foreach (var role in roles.EnumerateArray())
        {
            if (ulong.Parse(role.GetString()!) == config.ModeratorRoleId.Value)
                return true;
        }
        return false;
    }
}
