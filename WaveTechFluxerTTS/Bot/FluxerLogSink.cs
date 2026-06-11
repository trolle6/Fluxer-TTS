using System.Collections.Concurrent;
using WaveTechFluxerTTS.Fluxer;

namespace WaveTechFluxerTTS.Bot;

public sealed class FluxerLogSink
{
    private readonly FluxerRestApi _rest;
    private readonly ulong? _logChannelId;
    private readonly ConcurrentDictionary<string, DateTime> _dedupe = new();

    public FluxerLogSink(FluxerRestApi rest, BotConfig config)
    {
        _rest = rest;
        _logChannelId = config.LogChannelId;
    }

    public void Info(string message) => Console.WriteLine(message);

    public void Warning(string message) => Log("WARN", message);

    public void Error(string message) => Log("ERROR", message);

    private void Log(string level, string message)
    {
        var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {level}: {message}";
        Console.WriteLine(line);
        if (_logChannelId is null)
            return;
        var key = $"{level}:{message}";
        if (_dedupe.TryGetValue(key, out var last) && DateTime.UtcNow - last < TimeSpan.FromSeconds(60))
            return;
        _dedupe[key] = DateTime.UtcNow;
        _ = Task.Run(async () =>
        {
            try
            {
                await _rest.SendMessageAsync(_logChannelId.Value, line, CancellationToken.None);
            }
            catch { /* ignore log failures */ }
        });
    }
}
