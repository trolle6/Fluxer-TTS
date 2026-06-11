using System.Text.Json;
using WaveTechFluxerTTS.Bot;
using WaveTechFluxerTTS.Fluxer;
using WaveTechFluxerTTS.SecretSanta.Storage;

namespace WaveTechFluxerTTS.SecretSanta;

public sealed class SecretSantaModule : IBotModule
{
    public string Name => "SecretSanta";

    private BotContext? _context;
    private SecretSantaService? _service;
    private StateStore? _stateStore;

    public Task RegisterAsync(BotContext context, CancellationToken cancellationToken)
    {
        _context = context;
        _stateStore = new StateStore(context.Config);
        var archive = new ArchiveStore(context.Config);
        _service = new SecretSantaService(_stateStore, archive, context.Config, context.Http, context.Rest);
        context.Services.Register<ISecretSantaParticipants>(_service);
        context.Services.Register(_service);

        context.Interactions.RegisterSlash("ss start", HandleStart);
        context.Interactions.RegisterSlash("ss status", HandleStatus);
        context.Interactions.RegisterSlash("ss shuffle", HandleShuffle);
        context.Interactions.RegisterSlash("ss stop", HandleStop);
        context.Interactions.RegisterSlash("ss ask_giftee", HandleAsk);
        context.Interactions.RegisterSlash("ss submit_gift", HandleSubmitGift);
        context.Interactions.RegisterSlash("ss giftee", HandleGiftee);
        context.Interactions.RegisterSlash("ss oversight", HandleOversight);
        context.Interactions.RegisterSlash("ss history", HandleHistory);
        context.Interactions.RegisterSlash("ss edit_gift", HandleEditGift);
        context.Interactions.RegisterSlash("ss archive", HandleArchive);
        context.Interactions.RegisterSlash("ss user_history", HandleUserHistory);
        context.Interactions.RegisterSlash("ss wishlist", HandleWishlist);

        context.Interactions.RegisterComponent("ss_reply:", HandleReplyButton);
        context.Interactions.RegisterComponent("ss_reply_modal", HandleReplyModal);

        context.Gateway.MessageReactionAdded += OnReactionAdd;
        context.Gateway.MessageReactionRemoved += OnReactionRemove;
        return Task.CompletedTask;
    }

    private async Task HandleStart(InteractionContext ctx, CancellationToken ct)
    {
        if (!ModPermissions.IsModerator(ctx, _context!.Config))
        {
            await _context.Rest.RespondEphemeralAsync(ctx, "Moderator only.", ct);
            return;
        }
        if (ctx.GuildId is null || ctx.ChannelId is null)
        {
            await _context.Rest.RespondEphemeralAsync(ctx, "Guild channel required.", ct);
            return;
        }
        await _context.Rest.DeferEphemeralAsync(ctx, ct);
        try
        {
            ulong msgId;
            if (ulong.TryParse(ctx.GetNestedString("message_id"), out var existingId))
                msgId = existingId;
            else
            {
                var message = ctx.GetNestedString("message") ?? "React to join Secret Santa! 🎄";
                msgId = await _context.Rest.CreateMessageAsync(ctx.ChannelId.Value, message, ct);
            }
            ulong? roleId = ulong.TryParse(ctx.GetNestedString("role_id"), out var rid) ? rid : null;
            await _service!.StartEventAsync(ctx.GuildId.Value, ctx.ChannelId.Value, msgId, roleId, ct);
            await _context.Rest.EditOriginalResponseAsync(ctx,
                content: $"Event started on message `{msgId}`. Users react to join.", cancellationToken: ct);
        }
        catch (Exception ex)
        {
            await _context.Rest.EditOriginalResponseAsync(ctx, content: ex.Message, cancellationToken: ct);
        }
    }

    private async Task HandleStatus(InteractionContext ctx, CancellationToken ct)
    {
        if (!ModPermissions.IsModerator(ctx, _context!.Config))
        {
            await _context.Rest.RespondEphemeralAsync(ctx, "Moderator only.", ct);
            return;
        }
        var state = await _service!.GetStateAsync(ct);
        var ev = state.CurrentEvent;
        if (ev is null)
        {
            await _context.Rest.RespondEphemeralAsync(ctx, "No active event.", ct);
            return;
        }
        await _context.Rest.RespondEphemeralAsync(ctx,
            $"**Secret Santa {state.CurrentYear}**\nParticipants: {ev.Participants.Count}\n" +
            $"Assignments: {ev.Assignments.Count}\nJoin closed: {ev.JoinClosed}",
            ct);
    }

    private async Task HandleShuffle(InteractionContext ctx, CancellationToken ct)
    {
        if (!ModPermissions.IsModerator(ctx, _context!.Config))
        {
            await _context.Rest.RespondEphemeralAsync(ctx, "Moderator only.", ct);
            return;
        }
        await _context.Rest.DeferEphemeralAsync(ctx, ct);
        try
        {
            var n = await _service!.ShuffleAndNotifyAsync(ct);
            await _context.Rest.EditOriginalResponseAsync(ctx, content: $"Shuffled and DMed {n} participants.", cancellationToken: ct);
        }
        catch (Exception ex)
        {
            await _context.Rest.EditOriginalResponseAsync(ctx, content: ex.Message, cancellationToken: ct);
        }
    }

    private async Task HandleStop(InteractionContext ctx, CancellationToken ct)
    {
        if (!ModPermissions.IsModerator(ctx, _context!.Config))
        {
            await _context.Rest.RespondEphemeralAsync(ctx, "Moderator only.", ct);
            return;
        }
        await _context.Rest.DeferEphemeralAsync(ctx, ct);
        try
        {
            await _service!.StopAndArchiveAsync(ct);
            await _context.Rest.EditOriginalResponseAsync(ctx, content: "Event stopped and archived.", cancellationToken: ct);
        }
        catch (Exception ex)
        {
            await _context.Rest.EditOriginalResponseAsync(ctx, content: ex.Message, cancellationToken: ct);
        }
    }

    private async Task HandleAsk(InteractionContext ctx, CancellationToken ct)
    {
        var q = ctx.GetNestedString("question") ?? "";
        await _context!.Rest.DeferEphemeralAsync(ctx, ct);
        try
        {
            var msg = await _service!.AskGifteeAsync(ctx.UserId, q, ct);
            await _context.Rest.EditOriginalResponseAsync(ctx, content: msg, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            await _context.Rest.EditOriginalResponseAsync(ctx, content: ex.Message, cancellationToken: ct);
        }
    }

    private async Task HandleSubmitGift(InteractionContext ctx, CancellationToken ct)
    {
        var gift = ctx.GetNestedString("gift") ?? "";
        await _context!.Rest.DeferEphemeralAsync(ctx, ct);
        try
        {
            await _service!.SubmitGiftAsync(ctx.UserId, gift, ct);
            await _context.Rest.EditOriginalResponseAsync(ctx, content: "Gift logged.", cancellationToken: ct);
        }
        catch (Exception ex)
        {
            await _context.Rest.EditOriginalResponseAsync(ctx, content: ex.Message, cancellationToken: ct);
        }
    }

    private async Task HandleGiftee(InteractionContext ctx, CancellationToken ct)
    {
        var state = await _service!.GetStateAsync(ct);
        var ev = state.CurrentEvent;
        if (ev is null || !ev.Assignments.TryGetValue(ctx.UserId.ToString(), out var gifteeId))
        {
            await _context!.Rest.RespondEphemeralAsync(ctx, "No assignment.", ct);
            return;
        }
        var list = ev.Wishlists.GetValueOrDefault(gifteeId, []);
        var name = ev.Participants.GetValueOrDefault(gifteeId, "Giftee");
        await _context.Rest.RespondEphemeralAsync(ctx,
            $"**{name}'s wishlist**\n{_service.FormatWishlist(list)}", ct);
    }

    private async Task HandleOversight(InteractionContext ctx, CancellationToken ct)
    {
        if (!ModPermissions.IsModerator(ctx, _context!.Config))
        {
            await _context.Rest.RespondEphemeralAsync(ctx, "Moderator only.", ct);
            return;
        }
        var view = ctx.GetNestedString("view") ?? "gifts";
        await _context.Rest.RespondEphemeralAsync(ctx, _service!.FormatOversight(view), ct);
    }

    private async Task HandleHistory(InteractionContext ctx, CancellationToken ct)
    {
        if (int.TryParse(ctx.GetNestedString("year"), out var year))
        {
            var archive = new ArchiveStore(_context!.Config).LoadArchive(year);
            if (archive is null)
            {
                await _context.Rest.RespondEphemeralAsync(ctx, $"No archive for {year}.", ct);
                return;
            }
            await _context.Rest.RespondEphemeralAsync(ctx,
                $"**{year}** — {archive.Event.Participants.Count} participants, {archive.Event.Assignments.Count} assignments.",
                ct);
            return;
        }
        await _context.Rest.RespondEphemeralAsync(ctx, _service!.FormatHistorySummary(), ct);
    }

    private async Task HandleEditGift(InteractionContext ctx, CancellationToken ct)
    {
        if (!int.TryParse(ctx.GetNestedString("year"), out var year))
        {
            await _context!.Rest.RespondEphemeralAsync(ctx, "Year required.", ct);
            return;
        }
        var gift = ctx.GetNestedString("gift_description") ?? "";
        await _context.Rest.DeferEphemeralAsync(ctx, ct);
        try
        {
            await _service!.EditGiftAsync(ctx.UserId, year, gift, ct);
            await _context.Rest.EditOriginalResponseAsync(ctx, content: $"Gift updated for {year}.", cancellationToken: ct);
        }
        catch (Exception ex)
        {
            await _context.Rest.EditOriginalResponseAsync(ctx, content: ex.Message, cancellationToken: ct);
        }
    }

    private async Task HandleArchive(InteractionContext ctx, CancellationToken ct)
    {
        if (!ModPermissions.IsModerator(ctx, _context!.Config))
        {
            await _context.Rest.RespondEphemeralAsync(ctx, "Moderator only.", ct);
            return;
        }
        var action = ctx.GetNestedString("action") ?? "";
        await _context.Rest.DeferEphemeralAsync(ctx, ct);
        try
        {
            if (action == "backups")
            {
                var list = _service!.FormatArchiveBackups();
                await _context.Rest.EditOriginalResponseAsync(ctx, content: $"**Backups:** {list}", cancellationToken: ct);
                return;
            }
            if (!int.TryParse(ctx.GetNestedString("year"), out var year))
            {
                await _context.Rest.EditOriginalResponseAsync(ctx, content: "Year required.", cancellationToken: ct);
                return;
            }
            if (action == "delete")
            {
                await _service!.ArchiveDeleteAsync(year, ct);
                await _context.Rest.EditOriginalResponseAsync(ctx, content: $"Moved {year} archive to backups.", cancellationToken: ct);
            }
            else if (action == "restore")
            {
                await _service!.ArchiveRestoreAsync(year, ct);
                await _context.Rest.EditOriginalResponseAsync(ctx, content: $"Restored {year} from backups.", cancellationToken: ct);
            }
            else
                await _context.Rest.EditOriginalResponseAsync(ctx, content: "Unknown action.", cancellationToken: ct);
        }
        catch (Exception ex)
        {
            await _context.Rest.EditOriginalResponseAsync(ctx, content: ex.Message, cancellationToken: ct);
        }
    }

    private async Task HandleUserHistory(InteractionContext ctx, CancellationToken ct)
    {
        if (!ModPermissions.IsModerator(ctx, _context!.Config))
        {
            await _context.Rest.RespondEphemeralAsync(ctx, "Moderator only.", ct);
            return;
        }
        var userId = ctx.GetUserId("user");
        if (userId is null)
        {
            await _context.Rest.RespondEphemeralAsync(ctx, "User required.", ct);
            return;
        }
        await _context.Rest.RespondEphemeralAsync(ctx, _service!.FormatUserHistory(userId.Value), ct);
    }

    private async Task HandleWishlist(InteractionContext ctx, CancellationToken ct)
    {
        var action = ctx.GetNestedString("action") ?? "view";
        var item = ctx.GetNestedString("item");
        await _context!.Rest.DeferEphemeralAsync(ctx, ct);
        try
        {
            await _service!.WishlistAsync(ctx.UserId, action, item, ct);
            var state = await _service.GetStateAsync(ct);
            var list = state.CurrentEvent!.Wishlists.GetValueOrDefault(ctx.UserId.ToString(), []);
            await _context.Rest.EditOriginalResponseAsync(ctx,
                content: $"**Your wishlist**\n{_service.FormatWishlist(list)}", cancellationToken: ct);
        }
        catch (Exception ex)
        {
            await _context.Rest.EditOriginalResponseAsync(ctx, content: ex.Message, cancellationToken: ct);
        }
    }

    private async Task HandleReplyButton(InteractionContext ctx, CancellationToken ct)
    {
        await _context!.Rest.RespondToInteractionAsync(ctx.InteractionId, ctx.InteractionToken,
            new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = InteractionResponseType.Modal,
                ["data"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["custom_id"] = $"ss_reply_modal:{ctx.CustomId?.Split(':').LastOrDefault()}",
                    ["title"] = "Reply to Santa",
                    ["components"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
                    {
                        ["type"] = 1,
                        ["components"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
                        {
                            ["type"] = 4,
                            ["custom_id"] = "reply_text",
                            ["label"] = "Your reply",
                            ["style"] = 2,
                            ["required"] = true,
                            ["max_length"] = 500
                        })
                    })
                }
            }, ct);
    }

    private async Task HandleReplyModal(InteractionContext ctx, CancellationToken ct)
    {
        var santaIdStr = ctx.CustomId?.Split(':').LastOrDefault();
        if (!ulong.TryParse(santaIdStr, out var santaId))
        {
            await _context!.Rest.RespondEphemeralAsync(ctx, "Invalid modal.", ct);
            return;
        }
        string? reply = null;
        if (ctx.Data.TryGetProperty("components", out var comps))
        {
            foreach (var row in comps.EnumerateArray())
            {
                foreach (var comp in row.GetProperty("components").EnumerateArray())
                {
                    if (comp.GetProperty("custom_id").GetString() == "reply_text")
                        reply = comp.GetProperty("value").GetString();
                }
            }
        }
        if (string.IsNullOrWhiteSpace(reply))
        {
            await _context.Rest.RespondEphemeralAsync(ctx, "Reply required.", ct);
            return;
        }
        await _context.Rest.DeferEphemeralAsync(ctx, ct);
        try
        {
            var msg = await _service!.ReplyToSantaAsync(ctx.UserId, santaId, reply, ct);
            await _context.Rest.EditOriginalResponseAsync(ctx, content: msg, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            await _context.Rest.EditOriginalResponseAsync(ctx, content: ex.Message, cancellationToken: ct);
        }
    }

    private async Task OnReactionAdd(JsonElement data)
    {
        if (!data.TryGetProperty("user_id", out var uidEl)) return;
        var userId = ulong.Parse(uidEl.GetString()!);
        if (_context!.Gateway.BotUserId == userId) return;
        var state = await _service!.GetStateAsync(default);
        var ev = state.CurrentEvent;
        if (ev is null || !ev.Active) return;
        if (!data.TryGetProperty("message_id", out var mid) ||
            ulong.Parse(mid.GetString()!) != ev.AnnouncementMessageId)
            return;
        var name = data.TryGetProperty("member", out var m) && m.TryGetProperty("nick", out var nick)
            ? nick.GetString() : userId.ToString();
        await _service.JoinParticipantAsync(userId, name ?? userId.ToString(), default);
    }

    private async Task OnReactionRemove(JsonElement data)
    {
        if (!data.TryGetProperty("user_id", out var uidEl)) return;
        var userId = ulong.Parse(uidEl.GetString()!);
        if (!data.TryGetProperty("guild_id", out var gidEl)) return;
        var guildId = ulong.Parse(gidEl.GetString()!);
        var state = await _service!.GetStateAsync(default);
        var ev = state.CurrentEvent;
        if (ev is null) return;
        if (!data.TryGetProperty("message_id", out var mid) ||
            ulong.Parse(mid.GetString()!) != ev.AnnouncementMessageId)
            return;
        await _service.LeaveParticipantAsync(guildId, userId, default);
    }
}
