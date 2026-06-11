namespace WaveTechFluxerTTS.SecretSanta.Assignments;

public static class AssignmentEngine
{
    public static Dictionary<ulong, ulong>? Shuffle(
        IReadOnlyList<ulong> participants,
        Dictionary<string, List<long>> history)
    {
        if (participants.Count < 2)
            return null;

        var ids = participants.ToList();
        for (var attempt = 0; attempt < 500; attempt++)
        {
            var shuffled = ids.OrderBy(_ => Random.Shared.Next()).ToList();
            var assignments = new Dictionary<ulong, ulong>();
            var valid = true;
            for (var i = 0; i < ids.Count; i++)
            {
                var giver = ids[i];
                var receiver = shuffled[i];
                if (giver == receiver)
                {
                    valid = false;
                    break;
                }
                if (history.TryGetValue(giver.ToString(), out var past) && past.Contains((long)receiver))
                {
                    valid = false;
                    break;
                }
                assignments[giver] = receiver;
            }
            if (valid)
                return assignments;
        }
        return null;
    }
}
