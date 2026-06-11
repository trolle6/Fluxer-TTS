using WaveTechFluxerTTS.Bot;
using WaveTechFluxerTTS.Utils;

namespace WaveTechFluxerTTS.SecretSanta.Storage;

public sealed class StateStore
{
    private readonly string _statePath;
    private readonly string _backupPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public StateStore(BotConfig config)
    {
        _statePath = config.StateFilePath;
        _backupPath = _statePath + ".backup";
    }

    public async Task<SecretSantaState> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var state = AtomicJsonFile.Load(_statePath, new SecretSantaState());
            if (state.CurrentEvent is null && File.Exists(_backupPath))
                state = AtomicJsonFile.Load(_backupPath, new SecretSantaState());
            if (state.CurrentYear < 2000 || state.CurrentYear > 2100)
                state.CurrentYear = DateTime.UtcNow.Year;
            return state;
        }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(SecretSantaState state, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (File.Exists(_statePath))
                File.Copy(_statePath, _backupPath, overwrite: true);
            await AtomicJsonFile.SaveAsync(_statePath, state, ct);
        }
        finally { _lock.Release(); }
    }
}
