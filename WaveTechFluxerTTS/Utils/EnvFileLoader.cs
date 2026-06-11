namespace WaveTechFluxerTTS.Utils;

/// <summary>
/// Loads KEY=VALUE lines into the process environment (does not override existing vars).
/// Used for TrueNAS/NAS installs where the UI does not pass env vars reliably.
/// </summary>
public static class EnvFileLoader
{
    public static IReadOnlyList<string> LoadIntoEnvironment(params string[] paths)
    {
        var loaded = new List<string>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;
            LoadFile(path);
            loaded.Add(path);
        }
        return loaded;
    }

    private static void LoadFile(string path)
    {
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = line[..eq].Trim();
            if (key.Length == 0)
                continue;

            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
                value = value[1..^1];

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}
