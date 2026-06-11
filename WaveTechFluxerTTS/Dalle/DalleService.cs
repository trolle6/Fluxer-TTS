using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WaveTechFluxerTTS.Bot;
using WaveTechFluxerTTS.Utils;

namespace WaveTechFluxerTTS.Dalle;

public sealed class DalleService
{
    private const string Url = "https://api.openai.com/v1/images/generations";
    private readonly HttpClient _http;
    private readonly BotConfig _config;
    private readonly LruCache<string> _cache;
    private readonly RateLimiter _rateLimiter;

    public long CacheHits { get; private set; }
    public long TotalGenerated { get; private set; }

    public DalleService(HttpClient http, BotConfig config)
    {
        _http = http;
        _config = config;
        _cache = new LruCache<string>(config.MaxImageCacheEntries, TimeSpan.FromHours(1));
        _rateLimiter = new RateLimiter(config.RateLimitRequests, TimeSpan.FromSeconds(config.RateLimitWindowSeconds));
    }

    public async Task<string?> GenerateAsync(string prompt, string size, string quality, CancellationToken ct)
    {
        if (!_rateLimiter.TryAcquire("dalle-global"))
            return null;

        var key = $"{prompt}|{size}|{quality}";
        if (_cache.TryGet(key, out var cached) && cached is not null)
        {
            CacheHits++;
            return cached;
        }

        var payload = new
        {
            model = "dall-e-3",
            prompt,
            n = 1,
            size,
            quality,
            response_format = "url",
            style = "vivid"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.OpenAiApiKey);
        request.Content = JsonContent.Create(payload);

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var imageUrl = doc.RootElement.GetProperty("data")[0].GetProperty("url").GetString();
        if (imageUrl is not null)
        {
            _cache.Set(key, imageUrl);
            TotalGenerated++;
        }
        return imageUrl;
    }

    public void CleanupCache() { /* TTL handled on get */ }
}
