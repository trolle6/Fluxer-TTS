using LiveKit.Rtc;
using WaveTechFluxerTTS.Fluxer;

namespace WaveTechFluxerTTS.Voice;

public sealed class GuildVoiceSession : IAsyncDisposable
{
    private readonly GatewayClient _gateway;
    private readonly ulong _guildId;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    private Room? _room;
    private AudioSource? _audioSource;
    private LocalAudioTrack? _audioTrack;
    private TaskCompletionSource<(string Endpoint, string Token)>? _voiceServerTcs;

    public GuildVoiceSession(ulong guildId, GatewayClient gateway)
    {
        _guildId = guildId;
        _gateway = gateway;
    }

    public void OnVoiceServerUpdate(string guildId, string endpoint, string token)
    {
        if (ulong.Parse(guildId) != _guildId)
            return;
        _voiceServerTcs?.TrySetResult((endpoint, token));
    }

    public async Task EnsureConnectedAsync(ulong channelId, CancellationToken cancellationToken)
    {
        if (_room is not null)
            return;

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (_room is not null)
                return;

            _voiceServerTcs = new TaskCompletionSource<(string, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _gateway.UpdateVoiceStateAsync(_guildId, channelId, cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
            var (endpoint, token) = await _voiceServerTcs.Task.WaitAsync(timeoutCts.Token);

            _room = new Room();
            await _room.ConnectAsync(endpoint, token, new RoomOptions { AutoSubscribe = false });

            _audioSource = new AudioSource(FfmpegMp3Streamer.SampleRate, FfmpegMp3Streamer.Channels);
            _audioTrack = LocalAudioTrack.Create("tts", _audioSource);
            await _room.LocalParticipant!.PublishTrackAsync(_audioTrack);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task PlayMp3Async(byte[] mp3, CancellationToken cancellationToken)
    {
        if (_audioSource is null)
            throw new InvalidOperationException("Voice session is not connected.");

        await using var streamer = new FfmpegMp3Streamer();
        await streamer.PlayMp3Async(mp3, async samples =>
        {
            var frame = new AudioFrame(
                samples,
                FfmpegMp3Streamer.SampleRate,
                FfmpegMp3Streamer.Channels,
                FfmpegMp3Streamer.SamplesPerFrame);
            await _audioSource.CaptureFrameAsync(frame);
        }, cancellationToken);
    }

    public bool IsConnected => _room is not null;

    public async Task DisconnectAsync()
    {
        if (_room is not null)
        {
            await _room.DisconnectAsync();
            _room = null;
        }
        _audioSource = null;
        _audioTrack = null;
        await _gateway.UpdateVoiceStateAsync(_guildId, null, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _connectLock.Dispose();
    }
}
