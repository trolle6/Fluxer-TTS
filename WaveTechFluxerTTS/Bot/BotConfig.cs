using Microsoft.Extensions.Configuration;

namespace WaveTechFluxerTTS.Bot;

public sealed class BotConfig
{
    public string FluxerBotToken { get; }
    public string FluxerApiBaseUrl { get; }
    public string FluxerApiVersion { get; }
    public string OpenAiApiKey { get; }
    public ulong? MainChannelId { get; }
    public ulong? LogChannelId { get; }
    public ulong? ModeratorRoleId { get; }
    public ulong? AllowedChannelId { get; }
    public ulong? TtsRoleId { get; }
    public int MaxQueueSize { get; }
    public int RateLimitRequests { get; }
    public int RateLimitWindowSeconds { get; }
    public int MaxAudioCacheEntries { get; }
    public int MaxImageCacheEntries { get; }
    public int AutoDisconnectTimeoutSeconds { get; }
    public string DefaultVoice { get; }
    public bool DebugMode { get; }
    public bool SkipApiValidation { get; }
    public bool SsDebugStart { get; }
    public string DataRoot { get; }

    public string StateFilePath => Path.Combine(DataRoot, "secret_santa_state.json");
    public string ArchiveDir => Path.Combine(DataRoot, "archive");
    public string ArchiveBackupsDir => Path.Combine(ArchiveDir, "backups");
    public string DistributedFilesDir => Path.Combine(DataRoot, "distributed_files");
    public string DistributedMetadataPath => Path.Combine(DataRoot, "distributed_files_metadata.json");

    public static readonly string[] AvailableVoices =
    [
        "alloy", "ash", "ballad", "coral", "echo", "fable", "nova",
        "onyx", "sage", "shimmer", "verse", "marin", "cedar"
    ];

    public BotConfig(IConfiguration configuration)
    {
        FluxerBotToken = GetRequired(configuration, "Fluxer:BotToken", "FLUXER_BOT_TOKEN");
        FluxerApiBaseUrl = configuration["Fluxer:ApiBaseUrl"] ?? "https://api.fluxer.app";
        FluxerApiVersion = configuration["Fluxer:ApiVersion"] ?? "1";
        OpenAiApiKey = GetRequired(configuration, "OpenAI:ApiKey", "OPENAI_API_KEY");

        MainChannelId = ParseOptionalUlong(configuration["Fluxer:MainChannelId"] ?? configuration["Tts:AllowedChannelId"]);
        LogChannelId = ParseOptionalUlong(configuration["Fluxer:LogChannelId"]);
        ModeratorRoleId = ParseOptionalUlong(configuration["Fluxer:ModeratorRoleId"]);
        AllowedChannelId = MainChannelId ?? ParseOptionalUlong(configuration["Tts:AllowedChannelId"]);
        TtsRoleId = ParseOptionalUlong(configuration["Tts:TtsRoleId"]);

        MaxQueueSize = configuration.GetValue("Tts:MaxQueueSize", 50);
        AutoDisconnectTimeoutSeconds = configuration.GetValue("Tts:AutoDisconnectTimeoutSeconds", 300);
        RateLimitRequests = configuration.GetValue("Tts:RateLimitRequests", 15);
        RateLimitWindowSeconds = configuration.GetValue("Tts:RateLimitWindowSeconds", 60);
        MaxAudioCacheEntries = configuration.GetValue("Tts:MaxAudioCacheEntries", 50);
        MaxImageCacheEntries = configuration.GetValue("Dalle:MaxCacheEntries", 30);
        DefaultVoice = configuration["Tts:DefaultVoice"] ?? "alloy";
        DebugMode = configuration.GetValue("DebugMode", false);
        SkipApiValidation = configuration.GetValue("SkipApiValidation", false);
        SsDebugStart = configuration.GetValue("SecretSanta:SsDebugStart", false);

        DataRoot = configuration["Data:Root"] ?? Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(ArchiveDir);
        Directory.CreateDirectory(ArchiveBackupsDir);
        Directory.CreateDirectory(DistributedFilesDir);
    }

    private static string GetRequired(IConfiguration configuration, string key, string envKey)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
            value = Environment.GetEnvironmentVariable(envKey);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Missing required setting '{key}' or environment variable '{envKey}'.");
        return value.Trim();
    }

    private static ulong? ParseOptionalUlong(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return ulong.TryParse(value.Trim(), out var id) ? id : null;
    }
}
