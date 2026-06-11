using WaveTechFluxerTTS.Bot;
using WaveTechFluxerTTS.Fluxer;
using WaveTechFluxerTTS.SecretSanta;
using WaveTechFluxerTTS.Utils;

namespace WaveTechFluxerTTS.Distribute;

public sealed class DistributeService
{
    private readonly BotConfig _config;
    private readonly FluxerRestApi _rest;
    private readonly SemaphoreSlim _dmRate = new(5, 5);

    public DistributeService(BotConfig config, FluxerRestApi rest)
    {
        _config = config;
        _rest = rest;
    }

    public DistributeMetadata LoadMetadata() =>
        AtomicJsonFile.Load(_config.DistributedMetadataPath, new DistributeMetadata());

    public async Task SaveMetadataAsync(DistributeMetadata data, CancellationToken ct) =>
        await AtomicJsonFile.SaveAsync(_config.DistributedMetadataPath, data, ct);

    public IReadOnlyList<ulong> GetTargets(BotServices services)
    {
        if (services.TryGet<ISecretSantaParticipants>(out var ss) && ss!.HasActiveEvent)
            return ss.GetParticipantIds();
        return [];
    }

    public async Task<int> DistributeToUsersAsync(IEnumerable<ulong> userIds, string message, CancellationToken ct)
    {
        var count = 0;
        foreach (var userId in userIds)
        {
            await _dmRate.WaitAsync(ct);
            try
            {
                await _rest.SendDmAsync(userId, message, ct);
                count++;
            }
            catch { /* skip failed DMs */ }
            finally
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000, ct);
                    _dmRate.Release();
                }, ct);
            }
        }
        return count;
    }
}
