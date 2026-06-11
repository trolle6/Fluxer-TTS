using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using WaveTechFluxerTTS.Bot;

namespace WaveTechFluxerTTS.Fluxer;

public sealed class FluxerRestApi
{
    private readonly HttpClient _http;
    private readonly BotConfig _config;
    private ulong? _applicationId;

    public FluxerRestApi(HttpClient http, BotConfig config)
    {
        _http = http;
        _config = config;
        _http.BaseAddress = new Uri($"{config.FluxerApiBaseUrl.TrimEnd('/')}/v{config.FluxerApiVersion}/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", config.FluxerBotToken);
    }

    public async Task<GatewayBotResponse> GetGatewayBotAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync("gateway/bot", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var url = doc.RootElement.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("Gateway URL missing.");
        var shards = doc.RootElement.TryGetProperty("shards", out var s) ? s.GetInt32() : 1;
        return new GatewayBotResponse(url, shards);
    }

    public async Task<ulong> GetApplicationIdAsync(CancellationToken cancellationToken)
    {
        if (_applicationId is not null)
            return _applicationId.Value;
        using var response = await _http.GetAsync("oauth2/applications/@me", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        _applicationId = ulong.Parse(doc.RootElement.GetProperty("id").GetString()!);
        return _applicationId.Value;
    }

    public async Task RegisterCommandsAsync(JsonNode[] commands, ulong? guildId, CancellationToken cancellationToken)
    {
        var appId = await GetApplicationIdAsync(cancellationToken);
        var json = JsonSerializer.Serialize(commands);

        var attempts = new List<string>
        {
            $"applications/{appId}/commands"
        };
        if (guildId is { } gid)
            attempts.Add($"applications/{appId}/guilds/{gid}/commands");
        attempts.Add("applications/@me/commands");

        foreach (var path in attempts)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Slash commands registered via {path}.");
                return;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"Command registration {path} failed: {(int)response.StatusCode} {body}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                continue;
            throw new InvalidOperationException($"Command registration failed: {(int)response.StatusCode} {body}");
        }

        Console.WriteLine("Slash command API not available on this Fluxer instance (404). Auto-TTS and message handling still work.");
    }

    public async Task RespondToInteractionAsync(
        string interactionId,
        string interactionToken,
        JsonNode body,
        CancellationToken cancellationToken)
    {
        var json = body.ToJsonString();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"interactions/{interactionId}/{interactionToken}/callback")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"Interaction response failed: {(int)response.StatusCode} {err}");
        }
    }

    public Task RespondEphemeralAsync(InteractionContext ctx, string content, CancellationToken ct) =>
        RespondToInteractionAsync(ctx.InteractionId, ctx.InteractionToken,
            new JsonObject
            {
                ["type"] = InteractionResponseType.ChannelMessageWithSource,
                ["data"] = new JsonObject
                {
                    ["content"] = content,
                    ["flags"] = 64
                }
            }, ct);

    public Task DeferEphemeralAsync(InteractionContext ctx, CancellationToken ct) =>
        RespondToInteractionAsync(ctx.InteractionId, ctx.InteractionToken,
            new JsonObject
            {
                ["type"] = InteractionResponseType.DeferredChannelMessageWithSource,
                ["data"] = new JsonObject { ["flags"] = 64 }
            }, ct);

    public Task DeferPublicAsync(InteractionContext ctx, CancellationToken ct) =>
        RespondToInteractionAsync(ctx.InteractionId, ctx.InteractionToken,
            new JsonObject { ["type"] = InteractionResponseType.DeferredChannelMessageWithSource },
            ct);

    public async Task EditOriginalResponseAsync(
        InteractionContext ctx,
        string? content = null,
        JsonNode? embeds = null,
        CancellationToken cancellationToken = default)
    {
        var appId = await GetApplicationIdAsync(cancellationToken);
        var data = new JsonObject();
        if (content is not null) data["content"] = content;
        if (embeds is not null) data["embeds"] = embeds;
        var json = data.ToJsonString();
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"webhooks/{appId}/{ctx.InteractionToken}/messages/@original")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"Edit original failed: {(int)response.StatusCode} {err}");
        }
    }

    public async Task SendMessageAsync(ulong channelId, string content, CancellationToken cancellationToken, JsonNode? embeds = null)
    {
        await TryCreateMessageAsync(channelId, content, cancellationToken, embeds);
    }

    public async Task<ulong?> TryCreateMessageAsync(ulong channelId, string content, CancellationToken cancellationToken, JsonNode? embeds = null)
    {
        try
        {
            return await CreateMessageAsync(channelId, content, cancellationToken, embeds);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"POST channels/{channelId}/messages failed: {ex.Message}");
            return null;
        }
    }

    public async Task<ulong> CreateMessageAsync(ulong channelId, string content, CancellationToken cancellationToken, JsonNode? embeds = null)
    {
        var payload = new JsonObject { ["content"] = content };
        if (embeds is not null) payload["embeds"] = embeds;
        using var response = await PostJsonRawAsync($"channels/{channelId}/messages", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ulong.Parse(doc.RootElement.GetProperty("id").GetString()!);
    }

    public async Task<ulong> CreateDmChannelAsync(ulong userId, CancellationToken cancellationToken)
    {
        var payload = new JsonObject { ["recipient_id"] = userId.ToString() };
        using var response = await PostJsonRawAsync("users/@me/channels", payload, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ulong.Parse(doc.RootElement.GetProperty("id").GetString()!);
    }

    public async Task SendDmAsync(ulong userId, string content, CancellationToken cancellationToken, JsonNode? components = null)
    {
        var channelId = await CreateDmChannelAsync(userId, cancellationToken);
        var payload = new JsonObject { ["content"] = content };
        if (components is not null) payload["components"] = components;
        await PostJsonAsync($"channels/{channelId}/messages", payload, cancellationToken);
    }

    public async Task AddGuildMemberRoleAsync(ulong guildId, ulong userId, ulong roleId, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsync(
            $"guilds/{guildId}/members/{userId}/roles/{roleId}",
            null,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            Console.WriteLine($"Add role failed: {(int)response.StatusCode}");
    }

    public async Task RemoveGuildMemberRoleAsync(ulong guildId, ulong userId, ulong roleId, CancellationToken cancellationToken)
    {
        using var response = await _http.DeleteAsync(
            $"guilds/{guildId}/members/{userId}/roles/{roleId}",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            Console.WriteLine($"Remove role failed: {(int)response.StatusCode}");
    }

    public async Task<byte[]> DownloadUrlAsync(string url, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        return await client.GetByteArrayAsync(url, cancellationToken);
    }

    public async Task SendMessageWithFileAsync(
        ulong channelId,
        string content,
        string filename,
        byte[] fileData,
        CancellationToken cancellationToken,
        bool ephemeralViaInteraction = false)
    {
        using var form = new MultipartFormDataContent();
        var payload = new JsonObject { ["content"] = content };
        form.Add(new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"), "payload_json");
        var fileContent = new ByteArrayContent(fileData);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "files[0]", filename);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"channels/{channelId}/messages") { Content = form };
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"File upload failed: {(int)response.StatusCode} {body}");
        }
    }

    public async Task SendDmWithFileAsync(ulong userId, string content, string filename, byte[] fileData, CancellationToken cancellationToken)
    {
        var channelId = await CreateDmChannelAsync(userId, cancellationToken);
        await SendMessageWithFileAsync(channelId, content, filename, fileData, cancellationToken);
    }

    public async Task EditOriginalWithFileAsync(
        InteractionContext ctx,
        string content,
        string filename,
        byte[] fileData,
        CancellationToken cancellationToken)
    {
        var appId = await GetApplicationIdAsync(cancellationToken);
        using var form = new MultipartFormDataContent();
        var payload = new JsonObject { ["content"] = content };
        form.Add(new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"), "payload_json");
        var fileContent = new ByteArrayContent(fileData);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "files[0]", filename);

        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"webhooks/{appId}/{ctx.InteractionToken}/messages/@original")
        { Content = form };
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Edit with file failed: {(int)response.StatusCode} {err}");
        }
    }

    public async Task ValidateOpenAiKeyAsync(CancellationToken cancellationToken)
    {
        if (_config.SkipApiValidation)
            return;
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.OpenAiApiKey);
        using var response = await client.GetAsync("https://api.openai.com/v1/models", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task PostJsonAsync(string path, JsonObject payload, CancellationToken cancellationToken)
    {
        using var response = await PostJsonRawAsync(path, payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"POST {path} failed: {(int)response.StatusCode} {body}");
        }
    }

    private Task<HttpResponseMessage> PostJsonRawAsync(string path, JsonObject payload, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };
        return _http.SendAsync(request, cancellationToken);
    }
}

public readonly record struct GatewayBotResponse(string Url, int Shards);
