using System.Diagnostics;

namespace WaveTechFluxerTTS.Voice;

/// <summary>
/// Decodes MP3 bytes to 48 kHz stereo PCM via ffmpeg (same approach as FluxerGroovy).
/// </summary>
public sealed class FfmpegMp3Streamer : IAsyncDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int FrameDurationMs = 20;
    public const int SamplesPerFrame = SampleRate * FrameDurationMs / 1000;
    public static readonly int BytesPerFrame = SamplesPerFrame * Channels * 2;

    private Process? _process;
    private Stream? _stdout;

    public async Task PlayMp3Async(
        byte[] mp3Data,
        Func<short[], Task> onFrame,
        CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"fluxer-tts-{Guid.NewGuid():N}.mp3");
        try
        {
            await File.WriteAllBytesAsync(tempFile, mp3Data, cancellationToken);
            await PlayFileAsync(tempFile, onFrame, cancellationToken);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* ignore */ }
        }
    }

    public async Task PlayFileAsync(
        string inputPath,
        Func<short[], Task> onFrame,
        CancellationToken cancellationToken)
    {
        var ffmpeg = FindFfmpeg();
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = $"-i \"{inputPath}\" -vn -f s16le -ar {SampleRate} -ac {Channels} pipe:1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ffmpeg.");
        _stdout = _process.StandardOutput.BaseStream;

        var buffer = new byte[BytesPerFrame * 4];
        var carry = new List<byte>();

        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await _stdout.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;

            for (var i = 0; i < read; i++)
                carry.Add(buffer[i]);

            while (carry.Count >= BytesPerFrame)
            {
                var frameBytes = carry.GetRange(0, BytesPerFrame).ToArray();
                carry.RemoveRange(0, BytesPerFrame);
                var samples = new short[SamplesPerFrame * Channels];
                Buffer.BlockCopy(frameBytes, 0, samples, 0, frameBytes.Length);
                await onFrame(samples);
            }
        }

        await _process.WaitForExitAsync(cancellationToken);
    }

    private static string FindFfmpeg()
    {
        var path = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            return path;

        var names = OperatingSystem.IsWindows() ? new[] { "ffmpeg.exe", "ffmpeg" } : new[] { "ffmpeg" };
        foreach (var name in names)
        {
            var found = FindOnPath(name);
            if (found is not null)
                return found;
        }

        throw new InvalidOperationException(
            "ffmpeg not found. Install ffmpeg and add it to PATH, or set FFMPEG_PATH.");
    }

    private static string? FindOnPath(string fileName)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var dir in paths)
        {
            var full = Path.Combine(dir.Trim(), fileName);
            if (File.Exists(full))
                return full;
        }
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            try { _process.Kill(entireProcessTree: true); } catch { /* ignore */ }
        }
        _process?.Dispose();
        if (_stdout is not null)
            await _stdout.DisposeAsync();
    }
}
