using Microsoft.Extensions.Configuration;
using WaveTechFluxerTTS.Bot;
using WaveTechFluxerTTS.Utils;

var dataRoot = Environment.GetEnvironmentVariable("DATA_ROOT")
    ?? Path.Combine(AppContext.BaseDirectory, "Data");

var envFiles = EnvFileLoader.LoadIntoEnvironment(
    Path.Combine(AppContext.BaseDirectory, "config.env"),
    Path.Combine(dataRoot, "config.env"),
    "/app/Data/config.env");

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("config.env.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FLUXER_BOT_TOKEN"))
    && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FLUXER_TOKEN")))
{
    Console.WriteLine("Missing FLUXER_BOT_TOKEN.");
    Console.WriteLine("TrueNAS: create config.env on your mounted Data volume, e.g.:");
    Console.WriteLine($"  {Path.Combine(dataRoot, "config.env")}");
    if (envFiles.Count > 0)
        Console.WriteLine($"  (checked: {string.Join(", ", envFiles)})");
    else
        Console.WriteLine("  (no config.env file found yet — copy Data/config.env.example)");
}

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
