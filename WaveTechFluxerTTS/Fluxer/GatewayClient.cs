using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using WaveTechFluxerTTS.Bot;

namespace WaveTechFluxerTTS.Fluxer;

public sealed class GatewayClient : IAsyncDisposable
{
    private const int Intents =
        (1 << 0) | (1 << 1) | (1 << 7) | (1 << 9) | (1 << 15) | (1 << 10);

    private readonly BotConfig _config;
    private readonly FluxerRestApi _rest;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveCts;
    private int? _heartbeatIntervalMs;
    private int _lastSequence;
    private Task? _heartbeatTask;
    private Task? _receiveTask;

    public ulong? BotUserId { get; private set; }
    public event Func<Task>? Ready;

    public event Func<JsonElement, Task>? MessageCreated;
    public event Func<JsonElement, Task>? VoiceStateUpdated;
    public event Func<JsonElement, Task>? GuildCreated;
    public event Func<string, string, string, Task>? VoiceServerUpdated;
    public event Func<JsonElement, Task>? InteractionCreated;
    public event Func<JsonElement, Task>? MessageReactionAdded;
    public event Func<JsonElement, Task>? MessageReactionRemoved;

    public GatewayClient(BotConfig config, FluxerRestApi rest)
    {
        _config = config;
        _rest = rest;
    }

    public async Task RunForeverAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectOnceAsync(cancellationToken);
                if (_receiveTask is not null)
                    await _receiveTask;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Gateway error: {ex.Message}. Reconnecting in {delay.TotalSeconds}s...");
            }
            finally
            {
                await TeardownConnectionAsync();
            }

            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 1.5, 60));
        }
    }

    private async Task ConnectOnceAsync(CancellationToken cancellationToken)
    {
        var gateway = await _rest.GetGatewayBotAsync(cancellationToken);
        var wsUrl = $"{gateway.Url}?v={_config.FluxerApiVersion}&encoding=json";
        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(new Uri(wsUrl), cancellationToken);
        _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _receiveTask = ReceiveLoopAsync(_receiveCts.Token);
        Console.WriteLine("Gateway connected.");
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 256];
        while (_socket is { State: WebSocketState.Open } && !cancellationToken.IsCancellationRequested)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await _socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            await HandlePayloadAsync(Encoding.UTF8.GetString(ms.ToArray()), cancellationToken);
        }
    }

    private async Task HandlePayloadAsync(string json, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var op = root.GetProperty("op").GetInt32();

        switch (op)
        {
            case 10:
                _heartbeatIntervalMs = root.GetProperty("d").GetProperty("heartbeat_interval").GetInt32();
                await SendIdentifyAsync(cancellationToken);
                StartHeartbeat(cancellationToken);
                break;
            case 11:
                break;
            case 0:
                if (root.TryGetProperty("s", out var seq) && seq.ValueKind != JsonValueKind.Null)
                    _lastSequence = seq.GetInt32();
                await HandleDispatchAsync(root.GetProperty("t").GetString(), root.GetProperty("d"));
                break;
            case 9:
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                await SendIdentifyAsync(cancellationToken);
                break;
        }
    }

    private async Task HandleDispatchAsync(string? eventName, JsonElement data)
    {
        switch (eventName)
        {
            case "READY":
                BotUserId = ulong.Parse(data.GetProperty("user").GetProperty("id").GetString()!);
                Console.WriteLine($"Logged in as {data.GetProperty("user").GetProperty("username").GetString()} (id {BotUserId})");
                if (Ready is not null) await Ready();
                break;
            case "MESSAGE_CREATE":
                if (MessageCreated is not null) await MessageCreated(data);
                break;
            case "GUILD_CREATE":
                if (GuildCreated is not null) await GuildCreated(data);
                break;
            case "VOICE_STATE_UPDATE":
                if (VoiceStateUpdated is not null) await VoiceStateUpdated(data);
                break;
            case "VOICE_SERVER_UPDATE":
                if (VoiceServerUpdated is not null)
                    await VoiceServerUpdated(
                        data.GetProperty("guild_id").GetString()!,
                        data.GetProperty("endpoint").GetString()!,
                        data.GetProperty("token").GetString()!);
                break;
            case "INTERACTION_CREATE":
                if (InteractionCreated is not null) await InteractionCreated(data);
                break;
            case "MESSAGE_REACTION_ADD":
                if (MessageReactionAdded is not null) await MessageReactionAdded(data);
                break;
            case "MESSAGE_REACTION_REMOVE":
                if (MessageReactionRemoved is not null) await MessageReactionRemoved(data);
                break;
        }
    }

    private void StartHeartbeat(CancellationToken cancellationToken)
    {
        if (_heartbeatIntervalMs is null) return;
        _heartbeatTask = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_heartbeatIntervalMs.Value, cancellationToken);
                await SendHeartbeatAsync(cancellationToken);
            }
        }, cancellationToken);
    }

    private async Task SendIdentifyAsync(CancellationToken cancellationToken)
    {
        await SendJsonAsync(new
        {
            op = 2,
            d = new
            {
                token = _config.FluxerBotToken,
                intents = Intents,
                properties = new Dictionary<string, string>
                {
                    ["$os"] = "windows",
                    ["$browser"] = "WaveTechFluxerToolbox",
                    ["$device"] = "WaveTechFluxerToolbox"
                }
            }
        }, cancellationToken);
    }

    private Task SendHeartbeatAsync(CancellationToken cancellationToken) =>
        SendJsonAsync(new { op = 1, d = _lastSequence }, cancellationToken);

    public Task UpdateVoiceStateAsync(ulong guildId, ulong? channelId, CancellationToken cancellationToken) =>
        SendJsonAsync(new
        {
            op = 4,
            d = new
            {
                guild_id = guildId.ToString(),
                channel_id = channelId?.ToString(),
                self_deaf = true,
                self_mute = false
            }
        }, cancellationToken);

    private async Task SendJsonAsync(object payload, CancellationToken cancellationToken)
    {
        if (_socket is null || _socket.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task TeardownConnectionAsync()
    {
        _receiveCts?.Cancel();
        if (_heartbeatTask is not null)
        {
            try { await _heartbeatTask; }
            catch (OperationCanceledException) { }
            _heartbeatTask = null;
        }
        if (_receiveTask is not null)
        {
            try { await _receiveTask; }
            catch (OperationCanceledException) { }
            _receiveTask = null;
        }
        if (_socket is { State: WebSocketState.Open })
        {
            try { await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", CancellationToken.None); }
            catch { /* ignore */ }
        }
        _socket?.Dispose();
        _socket = null;
        _receiveCts?.Dispose();
        _receiveCts = null;
    }

    public async ValueTask DisposeAsync()
    {
        await TeardownConnectionAsync();
    }
}
