using System.Collections.Concurrent;
using System.Text.Json;
using WaveTechFluxerTTS.Bot;
using WaveTechFluxerTTS.Fluxer;
using WaveTechFluxerTTS.Utils;
using WaveTechFluxerTTS.Voice;

namespace WaveTechFluxerTTS.Tts;

public sealed class TtsCoordinator
{
    private readonly BotConfig _config;
    private readonly GatewayClient _gateway;
    private readonly OpenAiTtsService _tts;
    private readonly TextProcessor _textProcessor = new();
    private readonly RateLimiter _rateLimiter;
    private readonly ConcurrentDictionary<ulong, ulong?> _userVoiceChannels = new();
    private readonly ConcurrentDictionary<ulong, Dictionary<ulong, ulong?>> _guildVoiceStates = new();
    private readonly ConcurrentDictionary<ulong, string> _voiceAssignments = new();
    private readonly ConcurrentDictionary<ulong, DateTime> _nameAnnouncements = new();
    private readonly ConcurrentDictionary<ulong, GuildVoiceSession> _voiceSessions = new();
    private readonly ConcurrentDictionary<ulong, ConcurrentQueue<TtsQueueItem>> _queues = new();
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _queueLocks = new();

    private long _messagesProcessed;
    private long _chunksDropped;

    private const int NameAnnouncementCooldownSeconds = 7200;

    public TtsCoordinator(BotConfig config, GatewayClient gateway, OpenAiTtsService tts)
    {
        _config = config;
        _gateway = gateway;
        _tts = tts;
        _rateLimiter = new RateLimiter(config.RateLimitRequests, TimeSpan.FromSeconds(config.RateLimitWindowSeconds));
    }

    private readonly ConcurrentDictionary<ulong, DateTime> _guildLastActivity = new();

    public void Attach(GatewayClient gateway)
    {
        gateway.MessageCreated += OnMessageCreatedAsync;
        gateway.VoiceStateUpdated += OnVoiceStateUpdatedAsync;
        gateway.GuildCreated += OnGuildCreatedAsync;
        gateway.VoiceServerUpdated += OnVoiceServerUpdatedAsync;
        _ = IdleDisconnectLoopAsync();
    }

    private async Task IdleDisconnectLoopAsync()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromMinutes(1));
            var cutoff = DateTime.UtcNow.AddSeconds(-_config.AutoDisconnectTimeoutSeconds);
            foreach (var (guildId, last) in _guildLastActivity.ToArray())
            {
                if (last >= cutoff || !IsConnectedToVoice(guildId)) continue;
                if (GetGuildQueueCount(guildId) > 0) continue;
                await DisconnectGuildAsync(guildId);
                _guildLastActivity.TryRemove(guildId, out _);
            }
        }
    }

    public TtsStats GetStats() => new(
        Interlocked.Read(ref _messagesProcessed),
        Interlocked.Read(ref _chunksDropped),
        _tts.CacheHits,
        _tts.TotalRequests,
        _tts.TotalFailed,
        _queues.Values.Sum(q => q.Count));

    public int GetGuildQueueCount(ulong guildId) =>
        _queues.TryGetValue(guildId, out var q) ? q.Count : 0;

    public bool IsConnectedToVoice(ulong guildId) =>
        _voiceSessions.TryGetValue(guildId, out var s) && s.IsConnected;

    public async Task DisconnectGuildAsync(ulong guildId)
    {
        if (_voiceSessions.TryRemove(guildId, out var session))
            await session.DisposeAsync();
        await _gateway.UpdateVoiceStateAsync(guildId, null, CancellationToken.None);
    }

    public void ClearGuildQueue(ulong guildId)
    {
        if (_queues.TryGetValue(guildId, out var q))
            while (q.TryDequeue(out _)) { }
    }

    private Task OnGuildCreatedAsync(JsonElement data)
    {
        if (!data.TryGetProperty("voice_states", out var states)) return Task.CompletedTask;
        var guildId = ulong.Parse(data.GetProperty("id").GetString()!);
        var map = _guildVoiceStates.GetOrAdd(guildId, _ => new Dictionary<ulong, ulong?>());
        foreach (var vs in states.EnumerateArray())
        {
            var userId = ulong.Parse(vs.GetProperty("user_id").GetString()!);
            if (vs.TryGetProperty("channel_id", out var ch) && ch.ValueKind != JsonValueKind.Null)
            {
                var channelId = ulong.Parse(ch.GetString()!);
                map[userId] = channelId;
                _userVoiceChannels[userId] = channelId;
            }
        }
        return Task.CompletedTask;
    }

    private Task OnVoiceServerUpdatedAsync(string guildId, string endpoint, string token)
    {
        if (_voiceSessions.TryGetValue(ulong.Parse(guildId), out var session))
            session.OnVoiceServerUpdate(guildId, endpoint, token);
        return Task.CompletedTask;
    }

    private Task OnVoiceStateUpdatedAsync(JsonElement data)
    {
        var guildId = ulong.Parse(data.GetProperty("guild_id").GetString()!);
        var userId = ulong.Parse(data.GetProperty("user_id").GetString()!);
        if (_gateway.BotUserId == userId) return Task.CompletedTask;
        var map = _guildVoiceStates.GetOrAdd(guildId, _ => new Dictionary<ulong, ulong?>());
        if (data.TryGetProperty("channel_id", out var ch) && ch.ValueKind != JsonValueKind.Null)
            map[userId] = ulong.Parse(ch.GetString()!);
        else
        {
            map.Remove(userId);
            _voiceAssignments.TryRemove(userId, out _);
            _nameAnnouncements.TryRemove(userId, out _);
        }
        _userVoiceChannels[userId] = map.GetValueOrDefault(userId);
        return Task.CompletedTask;
    }

    private async Task OnMessageCreatedAsync(JsonElement data)
    {
        if (!data.TryGetProperty("guild_id", out var guildEl)) return;
        var author = data.GetProperty("author");
        if (author.TryGetProperty("bot", out var botFlag) && botFlag.GetBoolean()) return;

        var guildId = ulong.Parse(guildEl.GetString()!);
        var userId = ulong.Parse(author.GetProperty("id").GetString()!);
        var channelId = ulong.Parse(data.GetProperty("channel_id").GetString()!);

        if (_config.AllowedChannelId is { } allowed && channelId != allowed) return;
        if (!_userVoiceChannels.TryGetValue(userId, out var voiceChannelId) || voiceChannelId is null) return;

        if (_config.TtsRoleId is { } roleId)
        {
            if (!data.TryGetProperty("member", out var member) || !member.TryGetProperty("roles", out var roles)) return;
            var hasRole = roles.EnumerateArray().Any(r => ulong.Parse(r.GetString()!) == roleId);
            if (!hasRole) return;
        }

        if (!_rateLimiter.TryAcquire(userId.ToString())) return;

        var content = data.TryGetProperty("content", out var contentEl) ? contentEl.GetString() ?? "" : "";
        var cleaned = _textProcessor.Clean(content);
        if (string.IsNullOrWhiteSpace(cleaned)) return;

        if (_textProcessor.NeedsPronunciationHelp(cleaned))
            cleaned = await _tts.ImprovePronunciationAsync(cleaned, CancellationToken.None);

        var displayName = author.TryGetProperty("global_name", out var gn) && gn.ValueKind == JsonValueKind.String
            ? gn.GetString() : author.GetProperty("username").GetString();
        displayName ??= "Someone";

        var shouldAnnounce = !_nameAnnouncements.TryGetValue(userId, out var last) ||
            DateTime.UtcNow - last > TimeSpan.FromSeconds(NameAnnouncementCooldownSeconds);
        var speakText = shouldAnnounce ? $"{displayName} says: {cleaned}" : cleaned;
        if (shouldAnnounce) _nameAnnouncements[userId] = DateTime.UtcNow;

        var voice = GetVoiceForUser(userId);
        var queued = 0;
        foreach (var chunk in _textProcessor.SplitIntoChunks(speakText))
        {
            if (chunk.Length < 2) continue;
            if (TryEnqueue(guildId, new TtsQueueItem(userId, voiceChannelId.Value, chunk, voice)))
                queued++;
        }
        if (queued > 0)
        {
            Interlocked.Increment(ref _messagesProcessed);
            _guildLastActivity[guildId] = DateTime.UtcNow;
        }
    }

    private string GetVoiceForUser(ulong userId)
    {
        if (_voiceAssignments.TryGetValue(userId, out var assigned)) return assigned;
        var voice = BotConfig.AvailableVoices[(int)(userId % (ulong)BotConfig.AvailableVoices.Length)];
        _voiceAssignments[userId] = voice;
        return voice;
    }

    private bool TryEnqueue(ulong guildId, TtsQueueItem item)
    {
        var queue = _queues.GetOrAdd(guildId, _ => new ConcurrentQueue<TtsQueueItem>());
        if (queue.Count >= _config.MaxQueueSize)
        {
            Interlocked.Increment(ref _chunksDropped);
            return false;
        }
        queue.Enqueue(item);
        _ = ProcessQueueAsync(guildId);
        return true;
    }

    private async Task ProcessQueueAsync(ulong guildId)
    {
        var gate = _queueLocks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var queue = _queues.GetOrAdd(guildId, _ => new ConcurrentQueue<TtsQueueItem>());
            while (queue.TryDequeue(out var item))
            {
                if (DateTime.UtcNow - item.EnqueuedUtc > TimeSpan.FromMinutes(1)) continue;
                if (!_userVoiceChannels.TryGetValue(item.UserId, out var channelId) || channelId != item.VoiceChannelId) continue;

                var session = _voiceSessions.GetOrAdd(guildId, id => new GuildVoiceSession(id, _gateway));
                try { await session.EnsureConnectedAsync(item.VoiceChannelId, CancellationToken.None); }
                catch (Exception ex) { Console.WriteLine($"Voice connect failed: {ex.Message}"); continue; }

                var audio = await _tts.GenerateSpeechAsync(item.Text, item.Voice, CancellationToken.None);
                if (audio is null) continue;
                try { await session.PlayMp3Async(audio, CancellationToken.None); }
                catch (Exception ex) { Console.WriteLine($"Playback failed: {ex.Message}"); }
            }
        }
        finally { gate.Release(); }
    }

    public async Task ShutdownAsync()
    {
        foreach (var session in _voiceSessions.Values)
            await session.DisposeAsync();
        _voiceSessions.Clear();
    }

    private readonly record struct TtsQueueItem(ulong UserId, ulong VoiceChannelId, string Text, string Voice)
    {
        public DateTime EnqueuedUtc { get; init; } = DateTime.UtcNow;
    }
}

public readonly record struct TtsStats(
    long MessagesProcessed,
    long ChunksDropped,
    long CacheHits,
    long TotalRequests,
    long TotalFailed,
    int QueuedItems);
