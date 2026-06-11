namespace WaveTechFluxerTTS.Bot;

public sealed class DailyMaintenanceService
{
    private readonly IReadOnlyList<IBotModule> _modules;
    private CancellationTokenSource? _cts;

    public DailyMaintenanceService(IReadOnlyList<IBotModule> modules) => _modules = modules;

    public void Start(CancellationToken parentToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        _ = RunLoopAsync(_cts.Token);
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var next = now.Date.AddDays(1);
            var delay = next - now;
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            foreach (var module in _modules)
            {
                try
                {
                    await module.DailyMaintenanceAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Daily maintenance failed ({module.Name}): {ex.Message}");
                }
            }
        }
    }

    public void Stop() => _cts?.Cancel();
}
