using Microsoft.Extensions.Configuration;
using WaveTechFluxerTTS.Dalle;
using WaveTechFluxerTTS.Distribute;
using WaveTechFluxerTTS.Fluxer;
using WaveTechFluxerTTS.SecretSanta;
using WaveTechFluxerTTS.Tts;

namespace WaveTechFluxerTTS.Bot;

public sealed class BotHost : IAsyncDisposable
{
    private readonly BotConfig _config;
    private readonly HttpClient _http;
    private readonly FluxerRestApi _rest;
    private readonly GatewayClient _gateway;
    private readonly InteractionRouter _interactions;
    private readonly BotServices _services = new();
    private readonly List<IBotModule> _modules;
    private readonly FluxerLogSink _log;
    private readonly DailyMaintenanceService _maintenance;
    private readonly CancellationTokenSource _cts = new();

    public BotHost(IConfiguration configuration)
    {
        _config = new BotConfig(configuration);
        _http = new HttpClient();
        _rest = new FluxerRestApi(_http, _config);
        _gateway = new GatewayClient(_config, _rest);
        _interactions = new InteractionRouter();
        _log = new FluxerLogSink(_rest, _config);

        _modules =
        [
            new TtsModule(),
            new DalleModule(),
            new SecretSantaModule(),
            new DistributeModule()
        ];
        _maintenance = new DailyMaintenanceService(_modules);
    }

    public async Task RunAsync()
    {
        _log.Info("WaveTech Fluxer Toolbox — starting...");

        try
        {
            await _rest.ValidateOpenAiKeyAsync(_cts.Token);
            _log.Info("OpenAI API key validated.");
        }
        catch (Exception ex)
        {
            _log.Warning($"OpenAI validation skipped/failed: {ex.Message}");
        }

        var context = new BotContext
        {
            Config = _config,
            Http = _http,
            Rest = _rest,
            Gateway = _gateway,
            Interactions = _interactions,
            Services = _services,
            CancellationToken = _cts.Token
        };

        foreach (var module in _modules)
        {
            await module.RegisterAsync(context, _cts.Token);
            _log.Info($"Registered module: {module.Name}");
        }

        _gateway.InteractionCreated += data => _interactions.HandleAsync(data, _rest, _cts.Token);
        _gateway.Ready += OnReadyAsync;

        _maintenance.Start(_cts.Token);

        _ = Task.Run(() => _gateway.RunForeverAsync(_cts.Token), _cts.Token);
        _log.Info("Gateway running (auto-reconnect). Press Ctrl+C to stop.");

        try
        {
            await Task.Delay(Timeout.Infinite, _cts.Token);
        }
        catch (OperationCanceledException) { }
    }

    private async Task OnReadyAsync()
    {
        try
        {
            await _rest.RegisterCommandsAsync(CommandDefinitions.AllCommands, _config.GuildId, _cts.Token);
        }
        catch (Exception ex)
        {
            _log.Error($"Command registration failed: {ex.Message}");
        }

        if (_config.LogChannelId is { } logCh)
        {
            var sent = await _rest.TryCreateMessageAsync(logCh, "WaveTech Fluxer Toolbox is online.", _cts.Token);
            if (sent is null)
                _log.Warning("Could not post to log channel (403?). Check bot permissions in that channel.");
        }
    }

    public void Stop() => _cts.Cancel();

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _maintenance.Stop();
        foreach (var module in _modules)
            await module.ShutdownAsync(CancellationToken.None);
        await _gateway.DisposeAsync();
        _http.Dispose();
        _cts.Dispose();
    }
}
