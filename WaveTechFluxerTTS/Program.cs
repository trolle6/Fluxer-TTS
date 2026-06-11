using Microsoft.Extensions.Configuration;
using WaveTechFluxerTTS.Bot;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("config.env.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var delay = TimeSpan.FromSeconds(5);
while (true)
{
    await using var host = new BotHost(configuration);
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        host.Stop();
    };

    try
    {
        await host.RunAsync();
        break;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Bot crashed: {ex.Message}. Restarting in {delay.TotalSeconds}s...");
        await Task.Delay(delay);
        delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 1.5, 120));
    }
}
