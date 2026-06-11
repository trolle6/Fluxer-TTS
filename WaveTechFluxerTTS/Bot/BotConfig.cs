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
        FluxerBotToken = GetRequired(configuration, "Fluxer:BotToken", "FLUXER_BOT_TOKEN", "FLUXER_TOKEN");
        FluxerApiBaseUrl = ConfigOrEnv(configuration, "Fluxer:ApiBaseUrl", "FLUXER_API_URL") ?? "https://api.fluxer.app";
        FluxerApiVersion = ConfigOrEnv(configuration, "Fluxer:ApiVersion", "FLUXER_API_VERSION") ?? "1";
        OpenAiApiKey = GetRequired(configuration, "OpenAI:ApiKey", "OPENAI_API_KEY");

        MainChannelId = ParseOptionalUlong(
            ConfigOrEnv(configuration, "Fluxer:MainChannelId", "FLUXER_CHANNEL_ID")
            ?? ConfigOrEnv(configuration, "Tts:AllowedChannelId", "TTS_CHANNEL_ID"));
        LogChannelId = ParseOptionalUlong(ConfigOrEnv(configuration, "Fluxer:LogChannelId", "FLUXER_LOG_CHANNEL_ID"));
        ModeratorRoleId = ParseOptionalUlong(ConfigOrEnv(configuration, "Fluxer:ModeratorRoleId", "FLUXER_MODERATOR_ROLE_ID"));
        AllowedChannelId = MainChannelId ?? ParseOptionalUlong(ConfigOrEnv(configuration, "Tts:AllowedChannelId", "TTS_CHANNEL_ID"));
        TtsRoleId = ParseOptionalUlong(ConfigOrEnv(configuration, "Tts:TtsRoleId", "TTS_ROLE_ID"));

        MaxQueueSize = configuration.GetValue("Tts:MaxQueueSize", 50);
        AutoDisconnectTimeoutSeconds = configuration.GetValue("Tts:AutoDisconnectTimeoutSeconds", 300);
        RateLimitRequests = configuration.GetValue("Tts:RateLimitRequests", 15);
        RateLimitWindowSeconds = configuration.GetValue("Tts:RateLimitWindowSeconds", 60);
        MaxAudioCacheEntries = configuration.GetValue("Tts:MaxAudioCacheEntries", 50);
        MaxImageCacheEntries = configuration.GetValue("Dalle:MaxCacheEntries", 30);
        DefaultVoice = configuration["Tts:DefaultVoice"] ?? "alloy";
        DebugMode = configuration.GetValue("DebugMode", ParseBool(ConfigOrEnv(configuration, "DebugMode", "DEBUG_MODE"), false));
        SkipApiValidation = configuration.GetValue("SkipApiValidation", ParseBool(ConfigOrEnv(configuration, "SkipApiValidation", "SKIP_API_VALIDATION"), false));
        SsDebugStart = configuration.GetValue("SecretSanta:SsDebugStart", ParseBool(ConfigOrEnv(configuration, "SecretSanta:SsDebugStart", "SS_DEBUG_START"), false));

        DataRoot = ConfigOrEnv(configuration, "Data:Root", "DATA_ROOT")
            ?? Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(ArchiveDir);
        Directory.CreateDirectory(ArchiveBackupsDir);
        Directory.CreateDirectory(DistributedFilesDir);
    }

    private static string? ConfigOrEnv(IConfiguration configuration, string key, string envKey)
    {
        var value = configuration[key];
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();
        value = Environment.GetEnvironmentVariable(envKey);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string GetRequired(IConfiguration configuration, string key, params string[] envKeys)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            foreach (var envKey in envKeys)
            {
                value = Environment.GetEnvironmentVariable(envKey);
                if (!string.IsNullOrWhiteSpace(value))
                    break;
            }
        }
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Missing required setting '{key}' or environment variable(s): {string.Join(", ", envKeys)}.");
        return value.Trim();
    }

    private static ulong? ParseOptionalUlong(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return ulong.TryParse(value.Trim(), out var id) ? id : null;
    }

    private static bool ParseBool(string? value, bool defaultValue) =>
        string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value.Trim() switch
            {
                "1" or "true" or "True" or "yes" or "YES" => true,
                "0" or "false" or "False" or "no" or "NO" => false,
                _ => defaultValue
            };
}
