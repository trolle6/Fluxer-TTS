using System.Text.Json;
using WaveTechFluxerTTS.Fluxer;

namespace WaveTechFluxerTTS.Bot;

public sealed class InteractionRouter
{
    private readonly Dictionary<string, Func<InteractionContext, CancellationToken, Task>> _slashHandlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<InteractionContext, CancellationToken, Task>> _componentHandlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Func<InteractionContext, CancellationToken, Task>> _autocompleteHandlers = [];

    public void RegisterSlash(string qualifiedName, Func<InteractionContext, CancellationToken, Task> handler) =>
        _slashHandlers[qualifiedName] = handler;

    public void RegisterComponent(string customIdPrefix, Func<InteractionContext, CancellationToken, Task> handler) =>
        _componentHandlers[customIdPrefix] = handler;

    public void RegisterAutocomplete(Func<InteractionContext, CancellationToken, Task> handler) =>
        _autocompleteHandlers.Add(handler);

    public async Task HandleAsync(JsonElement data, FluxerRestApi rest, CancellationToken cancellationToken)
    {
        if (data.TryGetProperty("type", out var typeEl) && typeEl.GetInt32() == InteractionTypes.Ping)
        {
            await rest.RespondToInteractionAsync(
                data.GetProperty("id").GetString()!,
                data.GetProperty("token").GetString()!,
                new System.Text.Json.Nodes.JsonObject { ["type"] = 1 },
                cancellationToken);
            return;
        }

        var ctx = ParseInteraction(data);
        try
        {
            switch (ctx.Type)
            {
                case InteractionTypes.ApplicationCommand:
                    await HandleSlashAsync(ctx, rest, cancellationToken);
                    break;
                case InteractionTypes.MessageComponent:
                    await HandleComponentAsync(ctx, cancellationToken);
                    break;
                case InteractionTypes.Autocomplete:
                    foreach (var h in _autocompleteHandlers)
                        await h(ctx, cancellationToken);
                    break;
                case InteractionTypes.ModalSubmit:
                    if (_componentHandlers.TryGetValue("ss_reply_modal", out var modal))
                        await modal(ctx, cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Interaction error: {ex.Message}");
            try
            {
                await rest.RespondEphemeralAsync(ctx, $"Error: {ex.Message}", cancellationToken);
            }
            catch { /* already responded */ }
        }
    }

    private async Task HandleSlashAsync(InteractionContext ctx, FluxerRestApi rest, CancellationToken cancellationToken)
    {
        var key = BuildCommandKey(ctx);
        if (_slashHandlers.TryGetValue(key, out var handler))
            await handler(ctx, cancellationToken);
        else
        {
            Console.WriteLine($"Unhandled slash: {key}");
            await rest.RespondEphemeralAsync(ctx, $"Command `{key}` is not implemented yet.", cancellationToken);
        }
    }

    private async Task HandleComponentAsync(InteractionContext ctx, CancellationToken cancellationToken)
    {
        var customId = ctx.CustomId ?? "";
        foreach (var (prefix, handler) in _componentHandlers)
        {
            if (customId.StartsWith(prefix, StringComparison.Ordinal))
            {
                await handler(ctx, cancellationToken);
                return;
            }
        }
    }

    private static string BuildCommandKey(InteractionContext ctx)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(ctx.CommandName))
            parts.Add(ctx.CommandName);
        if (ctx.Data.ValueKind == JsonValueKind.Object &&
            ctx.Data.TryGetProperty("options", out var opts))
        {
            foreach (var opt in opts.EnumerateArray())
            {
                if (opt.GetProperty("type").GetInt32() == 1)
                    parts.Add(opt.GetProperty("name").GetString()!);
            }
        }
        return string.Join(" ", parts);
    }

    private static InteractionContext ParseInteraction(JsonElement data)
    {
        var id = data.GetProperty("id").GetString()!;
        var token = data.GetProperty("token").GetString()!;
        var type = data.GetProperty("type").GetInt32();
        ulong? guildId = data.TryGetProperty("guild_id", out var g) && g.ValueKind != JsonValueKind.Null
            ? ulong.Parse(g.GetString()!) : null;
        ulong? channelId = data.TryGetProperty("channel_id", out var c) && c.ValueKind != JsonValueKind.Null
            ? ulong.Parse(c.GetString()!) : null;
        JsonElement? member = data.TryGetProperty("member", out var m) ? m : null;
        ulong userId;
        if (member is not null)
            userId = ulong.Parse(member.Value.GetProperty("user").GetProperty("id").GetString()!);
        else
            userId = ulong.Parse(data.GetProperty("user").GetProperty("id").GetString()!);

        string? commandName = null;
        var options = new List<InteractionOption>();
        string? customId = null;
        JsonElement cmdData = default;

        if (data.TryGetProperty("data", out var d))
        {
            cmdData = d;
            if (d.TryGetProperty("name", out var n))
                commandName = n.GetString();
            if (d.TryGetProperty("custom_id", out var cid))
                customId = cid.GetString();
            if (d.TryGetProperty("options", out var opts))
                options.AddRange(ParseOptions(opts));
        }

        return new InteractionContext
        {
            InteractionId = id,
            InteractionToken = token,
            Type = type,
            GuildId = guildId,
            ChannelId = channelId,
            UserId = userId,
            Data = cmdData.ValueKind != JsonValueKind.Undefined ? cmdData : data,
            Root = data,
            Member = member,
            CommandName = commandName,
            Options = options,
            CustomId = customId
        };
    }

    private static IEnumerable<InteractionOption> ParseOptions(JsonElement opts)
    {
        foreach (var opt in opts.EnumerateArray())
        {
            var name = opt.GetProperty("name").GetString()!;
            var type = opt.GetProperty("type").GetInt32();
            string? str = opt.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            long? intVal = opt.TryGetProperty("value", out var iv) && iv.ValueKind == JsonValueKind.Number ? iv.GetInt64() : null;
            bool? boolVal = opt.TryGetProperty("value", out var bv) && bv.ValueKind is JsonValueKind.True or JsonValueKind.False ? bv.GetBoolean() : null;
            yield return new InteractionOption(name, type, str, intVal, boolVal);
            if (type == 1 && opt.TryGetProperty("options", out var nested))
            {
                foreach (var sub in ParseOptions(nested))
                    yield return sub;
            }
        }
    }
}

public static class InteractionContextExtensions
{
    public static string? GetString(this InteractionContext ctx, string name) =>
        ctx.Options.FirstOrDefault(o => o.Name == name).StringValue;

    public static bool GetBool(this InteractionContext ctx, string name, bool defaultValue = false) =>
        ctx.Options.FirstOrDefault(o => o.Name == name).BoolValue ?? defaultValue;

    public static ulong? GetUserId(this InteractionContext ctx, string name)
    {
        if (ctx.Data.ValueKind == JsonValueKind.Object &&
            ctx.Data.TryGetProperty("options", out var top))
        {
            foreach (var sub in top.EnumerateArray())
            {
                if (sub.GetProperty("type").GetInt32() != 1 ||
                    !sub.TryGetProperty("options", out var nested))
                    continue;
                foreach (var opt in nested.EnumerateArray())
                {
                    if (opt.GetProperty("name").GetString() != name ||
                        !opt.TryGetProperty("value", out var v))
                        continue;
                    if (v.ValueKind == JsonValueKind.String && ulong.TryParse(v.GetString(), out var id))
                        return id;
                }
            }
        }
        var flat = ctx.Options.FirstOrDefault(o => o.Name == name);
        if (flat.IntValue is { } i) return (ulong)i;
        if (ulong.TryParse(flat.StringValue, out var parsed)) return parsed;
        return null;
    }

    public static string GetSubcommand(this InteractionContext ctx)
    {
        if (ctx.Data.ValueKind == JsonValueKind.Object &&
            ctx.Data.TryGetProperty("options", out var opts))
        {
            foreach (var opt in opts.EnumerateArray())
            {
                if (opt.GetProperty("type").GetInt32() == 1)
                    return opt.GetProperty("name").GetString() ?? "";
            }
        }
        return "";
    }

    public static IReadOnlyList<ResolvedAttachment> GetAttachments(this InteractionContext ctx)
    {
        var list = new List<ResolvedAttachment>();
        if (!ctx.Root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("resolved", out var resolved) ||
            !resolved.TryGetProperty("attachments", out var attachments))
            return list;

        foreach (var att in attachments.EnumerateObject())
        {
            var el = att.Value;
            list.Add(new ResolvedAttachment(
                att.Name,
                el.GetProperty("filename").GetString()!,
                el.GetProperty("url").GetString()!,
                el.TryGetProperty("size", out var sz) ? sz.GetInt32() : 0));
        }
        return list;
    }

    public static string? GetNestedString(this InteractionContext ctx, string name)
    {
        if (ctx.Data.ValueKind != JsonValueKind.Object ||
            !ctx.Data.TryGetProperty("options", out var top))
            return ctx.GetString(name);
        foreach (var sub in top.EnumerateArray())
        {
            if (sub.GetProperty("type").GetInt32() != 1 ||
                !sub.TryGetProperty("options", out var nested))
                continue;
            foreach (var opt in nested.EnumerateArray())
            {
                if (opt.GetProperty("name").GetString() == name &&
                    opt.TryGetProperty("value", out var v))
                    return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
            }
        }
        return ctx.GetString(name);
    }
}
