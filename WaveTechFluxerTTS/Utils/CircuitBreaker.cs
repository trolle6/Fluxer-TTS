namespace WaveTechFluxerTTS.Utils;

public sealed class CircuitBreaker
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _recoveryTimeout;
    private int _failures;
    private DateTime? _openedAtUtc;

    public CircuitBreaker(int failureThreshold = 5, int recoveryTimeoutSeconds = 60)
    {
        _failureThreshold = failureThreshold;
        _recoveryTimeout = TimeSpan.FromSeconds(recoveryTimeoutSeconds);
    }

    public bool CanAttempt()
    {
        if (_openedAtUtc is null)
            return true;
        if (DateTime.UtcNow - _openedAtUtc.Value >= _recoveryTimeout)
            return true;
        return false;
    }

    public void RecordSuccess()
    {
        _failures = 0;
        _openedAtUtc = null;
    }

    public void RecordFailure()
    {
        _failures++;
        if (_failures >= _failureThreshold)
            _openedAtUtc = DateTime.UtcNow;
    }
}
