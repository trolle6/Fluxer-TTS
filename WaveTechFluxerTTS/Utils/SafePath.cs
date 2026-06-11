namespace WaveTechFluxerTTS.Utils;

public static class SafePath
{
    public static string? ResolveFileName(string directory, string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return null;
        var name = Path.GetFileName(filename.Trim());
        if (string.IsNullOrEmpty(name) || name is "." or "..")
            return null;
        var root = Path.GetFullPath(directory);
        var full = Path.GetFullPath(Path.Combine(root, name));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
