namespace WaveTechFluxerTTS.SecretSanta;

public interface ISecretSantaParticipants
{
    bool HasActiveEvent { get; }
    IReadOnlyList<ulong> GetParticipantIds();
    bool IsParticipant(ulong userId);
}
