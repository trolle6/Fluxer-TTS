using System.Text.Json;

namespace WaveTechFluxerTTS.Utils;

public static class AtomicJsonFile
{
    private const int MaxBytes = 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static T Load<T>(string path, T fallback) where T : class
    {
        if (!File.Exists(path))
            return fallback;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxBytes)
                return fallback;
            var text = File.ReadAllText(path).Trim();
            if (string.IsNullOrEmpty(text))
                return fallback;
            return JsonSerializer.Deserialize<T>(text) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static async Task SaveAsync<T>(string path, T data, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var temp = path + ".tmp";
        var json = JsonSerializer.Serialize(data, Options);
        await File.WriteAllTextAsync(temp, json, cancellationToken);
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temp, path);
    }

    public static void Save<T>(string path, T data)
    {
        SaveAsync(path, data).GetAwaiter().GetResult();
    }
}
