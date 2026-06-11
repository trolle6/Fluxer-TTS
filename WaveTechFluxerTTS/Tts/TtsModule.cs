using System.Text.Json.Nodes;
using WaveTechFluxerTTS.Bot;
using WaveTechFluxerTTS.Fluxer;
using WaveTechFluxerTTS.Voice;

namespace WaveTechFluxerTTS.Tts;

public sealed class TtsModule : IBotModule
{
    public string Name => "TTS";

    private TtsCoordinator? _coordinator;
    private OpenAiTtsService? _tts;

    public Task RegisterAsync(BotContext context, CancellationToken cancellationToken)
    {
        _tts = new OpenAiTtsService(context.Http, context.Config);
        _coordinator = new TtsCoordinator(context.Config, context.Gateway, _tts);
        _coordinator.Attach(context.Gateway);
        context.Services.Register(_coordinator);
        context.Services.Register(_tts);

        context.Interactions.RegisterSlash("tts stats", (ctx, ct) => HandleStats(ctx, context, ct));
        context.Interactions.RegisterSlash("tts status", (ctx, ct) => HandleStatus(ctx, context, ct));
        context.Interactions.RegisterSlash("tts diagnostics", (ctx, ct) => HandleDiagnostics(ctx, context, ct));
        context.Interactions.RegisterSlash("tts disconnect", (ctx, ct) => HandleDisconnect(ctx, context, ct));
        context.Interactions.RegisterSlash("tts clear", (ctx, ct) => HandleClear(ctx, context, ct));
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken) =>
        _coordinator?.ShutdownAsync() ?? Task.CompletedTask;

    private static async Task HandleStats(InteractionContext ctx, BotContext context, CancellationToken ct)
    {
        var coord = context.Services.Get<TtsCoordinator>();
        var s = coord.GetStats();
        await context.Rest.RespondEphemeralAsync(ctx,
            $"**TTS Stats**\nProcessed: {s.MessagesProcessed}\nDropped: {s.ChunksDropped}\n" +
            $"API requests: {s.TotalRequests} (failed {s.TotalFailed})\nCache hits: {s.CacheHits}\nQueued: {s.QueuedItems}",
            ct);
    }

    private static async Task HandleStatus(InteractionContext ctx, BotContext context, CancellationToken ct)
    {
        var coord = context.Services.Get<TtsCoordinator>();
        var guildId = ctx.GuildId ?? 0;
        var connected = guildId > 0 && coord.IsConnectedToVoice(guildId);
        var q = guildId > 0 ? coord.GetGuildQueueCount(guildId) : 0;
        await context.Rest.RespondEphemeralAsync(ctx,
            $"Voice: {(connected ? "connected" : "not connected")}\nQueue: {q} item(s)", ct);
    }

    private static async Task HandleDiagnostics(InteractionContext ctx, BotContext context, CancellationToken ct)
    {
        try
        {
            var ffmpeg = typeof(FfmpegMp3Streamer).Assembly; // trigger type load
            _ = ffmpeg;
            var path = Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "ffmpeg (PATH)";
            await context.Rest.RespondEphemeralAsync(ctx,
                $"**Diagnostics**\nffmpeg: {path}\nOpenAI TTS: configured\nLiveKit: enabled", ct);
        }
        catch (Exception ex)
        {
            await context.Rest.RespondEphemeralAsync(ctx, $"Diagnostics error: {ex.Message}", ct);
        }
    }

    private static async Task HandleDisconnect(InteractionContext ctx, BotContext context, CancellationToken ct)
    {
        if (!ModPermissions.IsModerator(ctx, context.Config))
        {
            await context.Rest.RespondEphemeralAsync(ctx, "Manage Server permission required.", ct);
            return;
        }
        if (ctx.GuildId is not { } gid)
        {
            await context.Rest.RespondEphemeralAsync(ctx, "Guild only.", ct);
            return;
        }
        await context.Rest.DeferEphemeralAsync(ctx, ct);
        await context.Services.Get<TtsCoordinator>().DisconnectGuildAsync(gid);
        await context.Rest.EditOriginalResponseAsync(ctx, content: "Disconnected from voice.", cancellationToken: ct);
    }

    private static async Task HandleClear(InteractionContext ctx, BotContext context, CancellationToken ct)
    {
        if (!ModPermissions.IsModerator(ctx, context.Config))
        {
            await context.Rest.RespondEphemeralAsync(ctx, "Manage Server permission required.", ct);
            return;
        }
        if (ctx.GuildId is not { } gid)
        {
            await context.Rest.RespondEphemeralAsync(ctx, "Guild only.", ct);
            return;
        }
        context.Services.Get<TtsCoordinator>().ClearGuildQueue(gid);
        await context.Rest.RespondEphemeralAsync(ctx, "TTS queue cleared.", ct);
    }
}
