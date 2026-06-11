using WaveTechFluxerTTS.Bot;
using WaveTechFluxerTTS.Fluxer;
using WaveTechFluxerTTS.SecretSanta;
using WaveTechFluxerTTS.Utils;

namespace WaveTechFluxerTTS.Distribute;

public sealed class DistributeModule : IBotModule
{
    public string Name => "DistributeZip";
    private const int MaxFileSize = 25 * 1024 * 1024;

    private BotContext? _context;
    private DistributeService? _service;

    public Task RegisterAsync(BotContext context, CancellationToken cancellationToken)
    {
        _context = context;
        _service = new DistributeService(context.Config, context.Rest);
        context.Services.Register(_service);

        context.Interactions.RegisterSlash("distribute upload", HandleUpload);
        context.Interactions.RegisterSlash("distribute list", HandleList);
        context.Interactions.RegisterSlash("distribute browse", HandleList);
        context.Interactions.RegisterSlash("distribute get", HandleGet);
        context.Interactions.RegisterSlash("distribute remove", HandleRemove);
        return Task.CompletedTask;
    }

    private bool RequireParticipant(InteractionContext ctx, out string? error)
    {
        error = null;
        if (_context!.Services.TryGet<ISecretSantaParticipants>(out var ss) && ss!.IsParticipant(ctx.UserId))
            return true;
        if (ss?.HasActiveEvent == false)
        {
            error = "No active Secret Santa event. Start one with /ss start first.";
            return false;
        }
        error = "You must be a Secret Santa participant.";
        return false;
    }

    private async Task HandleUpload(InteractionContext ctx, CancellationToken ct)
    {
        if (!RequireParticipant(ctx, out var err))
        {
            await _context!.Rest.RespondEphemeralAsync(ctx, err!, ct);
            return;
        }

        var attachments = ctx.GetAttachments();
        if (attachments.Count == 0)
        {
            await _context!.Rest.RespondEphemeralAsync(ctx,
                "Attach a file to this command (up to 25 MB), then run `/distribute upload` again.", ct);
            return;
        }

        var requiredBy = ctx.GetUserId("required_by") ?? ctx.UserId;
        await _context.Rest.DeferEphemeralAsync(ctx, ct);

        var meta = _service!.LoadMetadata();
        var ok = 0;
        var failed = new List<string>();

        foreach (var att in attachments)
        {
            if (att.Size > MaxFileSize)
            {
                failed.Add($"{att.Filename}: too large (max 25 MB)");
                continue;
            }

            try
            {
                var data = await _context.Rest.DownloadUrlAsync(att.Url, ct);
                var safePath = SafePath.ResolveFileName(_context.Config.DistributedFilesDir, att.Filename);
                if (safePath is null)
                {
                    failed.Add($"{att.Filename}: invalid name");
                    continue;
                }
                if (File.Exists(safePath))
                {
                    var stem = Path.GetFileNameWithoutExtension(safePath);
                    var ext = Path.GetExtension(safePath);
                    safePath = SafePath.ResolveFileName(_context.Config.DistributedFilesDir,
                        $"{stem}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}{ext}")!;
                }
                await File.WriteAllBytesAsync(safePath, data, ct);

                var fileId = Guid.NewGuid().ToString("N");
                meta.Files[fileId] = new DistributedFileEntry
                {
                    Name = Path.GetFileNameWithoutExtension(att.Filename),
                    Filename = Path.GetFileName(safePath),
                    UploadedBy = ctx.UserId,
                    RequiredBy = requiredBy,
                    UploadedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Size = data.Length
                };
                ok++;
            }
            catch (Exception ex)
            {
                failed.Add($"{att.Filename}: {ex.Message}");
            }
        }

        await _service.SaveMetadataAsync(meta, ct);

        var targets = _service.GetTargets(_context.Services);
        if (targets.Count > 0 && ok > 0)
        {
            var first = attachments[0];
            var fileData = await _context.Rest.DownloadUrlAsync(first.Url, ct);
            var sent = 0;
            foreach (var uid in targets)
            {
                try
                {
                    await _context.Rest.SendDmWithFileAsync(uid,
                        $"New shared file from <@{ctx.UserId}> (required by <@{requiredBy}>): **{first.Filename}**",
                        first.Filename, fileData, ct);
                    sent++;
                    await Task.Delay(1200, ct);
                }
                catch { /* skip */ }
            }
            await _context.Rest.EditOriginalResponseAsync(ctx,
                content: $"Uploaded {ok} file(s), notified {sent} participant(s)." +
                         (failed.Count > 0 ? $"\nFailed: {string.Join("; ", failed)}" : ""),
                cancellationToken: ct);
        }
        else
        {
            await _context.Rest.EditOriginalResponseAsync(ctx,
                content: $"Uploaded {ok} file(s)." + (failed.Count > 0 ? $"\nFailed: {string.Join("; ", failed)}" : ""),
                cancellationToken: ct);
        }
    }

    private async Task HandleList(InteractionContext ctx, CancellationToken ct)
    {
        if (!RequireParticipant(ctx, out var err))
        {
            await _context!.Rest.RespondEphemeralAsync(ctx, err!, ct);
            return;
        }
        var meta = _service!.LoadMetadata();
        if (meta.Files.Count == 0)
        {
            await _context!.Rest.RespondEphemeralAsync(ctx, "No files shared yet.", ct);
            return;
        }
        var lines = meta.Files.Values.Select(f =>
            $"• **{f.Name}** (`{f.Filename}`) — {f.Size / 1024} KB — required by <@{f.RequiredBy}>");
        await _context!.Rest.RespondEphemeralAsync(ctx, string.Join("\n", lines), ct);
    }

    private async Task HandleGet(InteractionContext ctx, CancellationToken ct)
    {
        if (!RequireParticipant(ctx, out var err))
        {
            await _context!.Rest.RespondEphemeralAsync(ctx, err!, ct);
            return;
        }
        var name = ctx.GetNestedString("filename") ?? "";
        var meta = _service!.LoadMetadata();
        var entry = meta.Files.Values.FirstOrDefault(f =>
            f.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            f.Filename.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            await _context!.Rest.RespondEphemeralAsync(ctx, "File not found.", ct);
            return;
        }
        var path = SafePath.ResolveFileName(_context!.Config.DistributedFilesDir, entry.Filename);
        if (path is null || !File.Exists(path))
        {
            await _context.Rest.RespondEphemeralAsync(ctx, "File missing on disk.", ct);
            return;
        }

        await _context.Rest.DeferEphemeralAsync(ctx, ct);
        var data = await File.ReadAllBytesAsync(path, ct);
        entry.DownloadCount++;
        await _service.SaveMetadataAsync(meta, ct);
        await _context.Rest.EditOriginalWithFileAsync(ctx,
            $"Here is **{entry.Name}**:", entry.Filename, data, ct);
    }

    private async Task HandleRemove(InteractionContext ctx, CancellationToken ct)
    {
        if (!ModPermissions.IsModerator(ctx, _context!.Config))
        {
            await _context.Rest.RespondEphemeralAsync(ctx, "Moderator only.", ct);
            return;
        }
        var name = ctx.GetNestedString("filename") ?? "";
        var meta = _service!.LoadMetadata();
        var key = meta.Files.FirstOrDefault(kv =>
            kv.Value.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            kv.Value.Filename.Equals(name, StringComparison.OrdinalIgnoreCase)).Key;
        if (key is null)
        {
            await _context.Rest.RespondEphemeralAsync(ctx, "File not found.", ct);
            return;
        }
        var entry = meta.Files[key];
        meta.Files.Remove(key);
        meta.History.Add(new DistributeHistoryEntry { FileId = key, FileName = entry.Name, Status = "*deleted*" });
        var path = SafePath.ResolveFileName(_context.Config.DistributedFilesDir, entry.Filename);
        if (path is not null && File.Exists(path))
            File.Delete(path);
        await _service.SaveMetadataAsync(meta, ct);
        await _context.Rest.RespondEphemeralAsync(ctx, "File removed.", ct);
    }
}
