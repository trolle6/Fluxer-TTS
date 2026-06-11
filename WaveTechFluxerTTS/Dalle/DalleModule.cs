using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using WaveTechFluxerTTS.Bot;
using WaveTechFluxerTTS.Fluxer;

namespace WaveTechFluxerTTS.Dalle;

public sealed class DalleModule : IBotModule
{
    public string Name => "DALL-E";

    private DalleService? _service;
    private BotContext? _context;
    private readonly ConcurrentQueue<ImageJob> _queue = new();
    private CancellationTokenSource? _workerCts;

    public Task RegisterAsync(BotContext context, CancellationToken cancellationToken)
    {
        _context = context;
        _service = new DalleService(context.Http, context.Config);
        context.Services.Register(_service);
        context.Interactions.RegisterSlash("image", HandleImageAsync);
        _workerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = ProcessQueueAsync(_workerCts.Token);
        return Task.CompletedTask;
    }

    public Task DailyMaintenanceAsync(CancellationToken cancellationToken)
    {
        _service?.CleanupCache();
        return Task.CompletedTask;
    }

    private async Task HandleImageAsync(InteractionContext ctx, CancellationToken ct)
    {
        var prompt = ctx.GetNestedString("prompt") ?? "";
        var size = ctx.GetNestedString("size") ?? "1024x1024";
        var quality = ctx.GetNestedString("quality") ?? "hd";
        var isPrivate = ctx.GetBool("private");

        if (prompt.Length < 3)
        {
            await _context!.Rest.RespondEphemeralAsync(ctx, "Prompt too short (min 3 characters).", ct);
            return;
        }

        if (isPrivate)
            await _context.Rest.DeferEphemeralAsync(ctx, ct);
        else
            await _context.Rest.DeferPublicAsync(ctx, ct);

        _queue.Enqueue(new ImageJob(ctx, prompt.Trim(), size, quality));
        await _context.Rest.EditOriginalResponseAsync(ctx, content: "Queued for generation...", cancellationToken: ct);
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!_queue.TryDequeue(out var job))
            {
                await Task.Delay(200, ct);
                continue;
            }

            if (job.Enqueued.AddMinutes(5) < DateTime.UtcNow)
                continue;

            var url = await _service!.GenerateAsync(job.Prompt, job.Size, job.Quality, ct);
            if (url is null)
            {
                await _context!.Rest.EditOriginalResponseAsync(job.Ctx,
                    content: "Generation failed or rate limited.", cancellationToken: ct);
                continue;
            }

            var embeds = new JsonArray(new JsonObject
            {
                ["title"] = "DALL-E 3",
                ["description"] = job.Prompt.Length > 200 ? job.Prompt[..200] + "..." : job.Prompt,
                ["image"] = new JsonObject { ["url"] = url },
                ["color"] = 0x5865F2
            });
            await _context.Rest.EditOriginalResponseAsync(job.Ctx, embeds: embeds, cancellationToken: ct);
        }
    }

    private readonly record struct ImageJob(InteractionContext Ctx, string Prompt, string Size, string Quality, DateTime Enqueued = default)
    {
        public DateTime Enqueued { get; } = Enqueued == default ? DateTime.UtcNow : Enqueued;
    }
}
