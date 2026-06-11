using WaveTechFluxerTTS.Fluxer;

namespace WaveTechFluxerTTS.Bot;

public sealed class BotContext
{
    public required BotConfig Config { get; init; }
    public required HttpClient Http { get; init; }
    public required FluxerRestApi Rest { get; init; }
    public required GatewayClient Gateway { get; init; }
    public required InteractionRouter Interactions { get; init; }
    public required BotServices Services { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

public sealed class BotServices
{
    private readonly Dictionary<Type, object> _services = new();

    public void Register<T>(T instance) where T : class => _services[typeof(T)] = instance;
    public T Get<T>() where T : class => (T)_services[typeof(T)];
    public bool TryGet<T>(out T? service) where T : class
    {
        if (_services.TryGetValue(typeof(T), out var obj))
        {
            service = (T)obj;
            return true;
        }
        service = null;
        return false;
    }
}
