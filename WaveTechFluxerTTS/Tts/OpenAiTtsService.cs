using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WaveTechFluxerTTS.Bot;
using WaveTechFluxerTTS.Utils;

namespace WaveTechFluxerTTS.Tts;

public sealed class OpenAiTtsService
{
    private const string TtsUrl = "https://api.openai.com/v1/audio/speech";
    private const string ChatUrl = "https://api.openai.com/v1/chat/completions";
    private const int MinValidAudioSize = 100;

    private readonly HttpClient _http;
    private readonly BotConfig _config;
    private readonly LruCache<byte[]> _audioCache;
    private readonly LruCache<string> _pronunciationCache;
    private readonly CircuitBreaker _circuitBreaker;

    public long CacheHits { get; private set; }
    public long TotalRequests { get; private set; }
    public long TotalFailed { get; private set; }

    public OpenAiTtsService(HttpClient http, BotConfig config)
    {
        _http = http;
        _config = config;
        _audioCache = new LruCache<byte[]>(config.MaxAudioCacheEntries, TimeSpan.FromHours(1));
        _pronunciationCache = new LruCache<string>(200, TimeSpan.FromHours(2));
        _circuitBreaker = new CircuitBreaker();
    }

    public async Task<byte[]?> GenerateSpeechAsync(string text, string voice, CancellationToken cancellationToken)
    {
        text = text.Trim();
        if (text.Length == 0 || !_circuitBreaker.CanAttempt())
            return null;

        if (!BotConfig.AvailableVoices.Contains(voice))
            voice = _config.DefaultVoice;

        var cacheKey = $"{voice}:{text}";
        if (_audioCache.TryGet(cacheKey, out var cached) && cached is not null)
        {
            CacheHits++;
            return cached;
        }

        var timeoutSeconds = Math.Clamp(
            60 + (text.Length / 100.0 * 0.15),
            60,
            180);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var payload = new
        {
            model = "tts-1-hd",
            input = text,
            voice,
            response_format = "mp3",
            speed = 1.0
        };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                TotalRequests++;
                using var request = new HttpRequestMessage(HttpMethod.Post, TtsUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.OpenAiApiKey);
                request.Content = JsonContent.Create(payload);

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var audio = await response.Content.ReadAsByteArrayAsync(cts.Token);
                    if (audio.Length < MinValidAudioSize)
                    {
                        _circuitBreaker.RecordFailure();
                        TotalFailed++;
                        return null;
                    }

                    _audioCache.Set(cacheKey, audio);
                    _circuitBreaker.RecordSuccess();
                    return audio;
                }

                if ((int)response.StatusCode is 429 or 500 or 502 or 503)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    if (attempt < 2)
                    {
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }
                }

                _circuitBreaker.RecordFailure();
                return null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _circuitBreaker.RecordFailure();
                return null;
            }
            catch (HttpRequestException) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }
        }

        _circuitBreaker.RecordFailure();
        return null;
    }

    public async Task<string> ImprovePronunciationAsync(string text, CancellationToken cancellationToken)
    {
        if (_pronunciationCache.TryGet(text, out var cached) && cached is not null)
            return cached;

        if (text.Length >= 3500)
            return text;

        var prompt =
            "Rewrite this text ONLY to improve pronunciation for text-to-speech. " +
            "Only expand very short acronyms (2-4 letters) into their letter names. " +
            "Convert complex usernames to speakable form. " +
            "Keep all other words exactly the same.\n\n" +
            $"Text: {text}\n\nImproved:";

        var payload = new
        {
            model = "gpt-3.5-turbo",
            messages = new[] { new { role = "user", content = prompt } },
            max_tokens = Math.Min(2000, Math.Max(200, (int)(text.Length / 4.0 * 1.5))),
            temperature = 0.1
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ChatUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.OpenAiApiKey);
            request.Content = JsonContent.Create(payload);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            using var response = await _http.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
                return text;

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
            var improved = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()?
                .Replace("Improved:", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            var finalText = string.IsNullOrWhiteSpace(improved) ? text : improved;
            _pronunciationCache.Set(text, finalText);
            return finalText;
        }
        catch
        {
            return text;
        }
    }
}
