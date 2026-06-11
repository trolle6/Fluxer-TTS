namespace WaveTechFluxerTTS.Bot;

public interface IBotModule
{
    string Name { get; }
    Task RegisterAsync(BotContext context, CancellationToken cancellationToken);
    Task RegisterCommandsAsync(BotContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    Task DailyMaintenanceAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
