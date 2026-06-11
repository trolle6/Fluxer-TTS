using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using WaveTechFluxerTTS.Bot;
using WaveTechFluxerTTS.Fluxer;
using WaveTechFluxerTTS.SecretSanta.Assignments;
using WaveTechFluxerTTS.SecretSanta.Storage;

namespace WaveTechFluxerTTS.SecretSanta;

public sealed class SecretSantaService : ISecretSantaParticipants
{
    private readonly StateStore _stateStore;
    private readonly ArchiveStore _archiveStore;
    private readonly BotConfig _config;
    private readonly HttpClient _http;
    private readonly FluxerRestApi _rest;

    public SecretSantaService(StateStore stateStore, ArchiveStore archiveStore, BotConfig config, HttpClient http, FluxerRestApi rest)
    {
        _stateStore = stateStore;
        _archiveStore = archiveStore;
        _config = config;
        _http = http;
        _rest = rest;
    }

    public bool HasActiveEvent
    {
        get
        {
            var state = _stateStore.LoadAsync().GetAwaiter().GetResult();
            return state.CurrentEvent is { Active: true };
        }
    }

    public IReadOnlyList<ulong> GetParticipantIds()
    {
        var state = _stateStore.LoadAsync().GetAwaiter().GetResult();
        if (state.CurrentEvent is null) return [];
        return state.CurrentEvent.Participants.Keys.Select(ulong.Parse).ToList();
    }

    public bool IsParticipant(ulong userId)
    {
        var state = _stateStore.LoadAsync().GetAwaiter().GetResult();
        return state.CurrentEvent?.Participants.ContainsKey(userId.ToString()) == true;
    }

    public async Task<SecretSantaState> GetStateAsync(CancellationToken ct) => await _stateStore.LoadAsync(ct);

    public async Task StartEventAsync(ulong guildId, ulong channelId, ulong messageId, ulong? roleId, CancellationToken ct)
    {
        var state = await _stateStore.LoadAsync(ct);
        var year = state.CurrentYear;
        if (!_config.SsDebugStart && _archiveStore.LoadArchive(year) is not null)
            throw new InvalidOperationException($"Archive for {year} already exists.");

        state.CurrentEvent = new SecretSantaEvent
        {
            Active = true,
            GuildId = guildId,
            AnnouncementChannelId = channelId,
            AnnouncementMessageId = messageId,
            RoleId = roleId,
            Participants = new Dictionary<string, string>()
        };
        await _stateStore.SaveAsync(state, ct);
    }

    public async Task<bool> JoinParticipantAsync(ulong userId, string displayName, CancellationToken ct)
    {
        var state = await _stateStore.LoadAsync(ct);
        var ev = state.CurrentEvent;
        if (ev is null || !ev.Active || ev.JoinClosed) return false;
        ev.Participants[userId.ToString()] = displayName;
        await _stateStore.SaveAsync(state, ct);
        if (ev.RoleId is { } roleId)
            await _rest.AddGuildMemberRoleAsync(ev.GuildId, userId, roleId, ct);
        return true;
    }

    public async Task<bool> LeaveParticipantAsync(ulong guildId, ulong userId, CancellationToken ct)
    {
        var state = await _stateStore.LoadAsync(ct);
        var ev = state.CurrentEvent;
        if (ev is null) return false;
        ev.Participants.Remove(userId.ToString());
        ev.Assignments.Remove(userId.ToString());
        await _stateStore.SaveAsync(state, ct);
        if (ev.RoleId is { } roleId)
            await _rest.RemoveGuildMemberRoleAsync(guildId, userId, roleId, ct);
        return true;
    }

    public async Task<int> ShuffleAndNotifyAsync(CancellationToken ct)
    {
        var state = await _stateStore.LoadAsync(ct);
        var ev = state.CurrentEvent ?? throw new InvalidOperationException("No active event.");
        var participants = ev.Participants.Keys.Select(ulong.Parse).ToList();
        var history = _archiveStore.LoadPairHistoryFromArchives();
        foreach (var (giver, receivers) in state.PairHistory)
            history[giver] = receivers;

        var assignments = AssignmentEngine.Shuffle(participants, history)
            ?? throw new InvalidOperationException("Could not generate valid assignments.");

        ev.Assignments.Clear();
        ev.JoinClosed = true;
        var count = 0;
        foreach (var (giver, receiver) in assignments)
        {
            ev.Assignments[giver.ToString()] = receiver.ToString();
            var receiverName = ev.Participants.GetValueOrDefault(receiver.ToString(), "your giftee");
            await _rest.SendDmAsync(giver,
                $"You are Secret Santa for **{receiverName}**! Use `/ss giftee` to see their wishlist.",
                ct);
            count++;
        }
        await _stateStore.SaveAsync(state, ct);
        return count;
    }

    public async Task StopAndArchiveAsync(CancellationToken ct)
    {
        var state = await _stateStore.LoadAsync(ct);
        var ev = state.CurrentEvent ?? throw new InvalidOperationException("No active event.");
        var archive = new YearArchive
        {
            Year = state.CurrentYear,
            ArchivedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Timestamp = DateTime.UtcNow.ToString("O"),
            Event = ev
        };
        await _archiveStore.SaveArchiveAsync(archive, ct);
        state.CurrentEvent = null;
        state.CurrentYear = state.CurrentYear + 1;
        await _stateStore.SaveAsync(state, ct);
    }

    public async Task<string> AskGifteeAsync(ulong santaId, string question, CancellationToken ct)
    {
        var state = await _stateStore.LoadAsync(ct);
        var ev = state.CurrentEvent ?? throw new InvalidOperationException("No active event.");
        if (!ev.Assignments.TryGetValue(santaId.ToString(), out var gifteeId))
            throw new InvalidOperationException("You have no assignment.");
        var anonymized = await AnonymizeAsync(question, ct);
        if (!ev.Communications.ContainsKey(santaId.ToString()))
            ev.Communications[santaId.ToString()] = new CommunicationThread { GifteeId = gifteeId };
        ev.Communications[santaId.ToString()].Thread.Add(new CommunicationMessage
        {
            From = "santa",
            Text = anonymized,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
        await _stateStore.SaveAsync(state, ct);

        var components = new JsonArray(new JsonObject
        {
            ["type"] = 1,
            ["components"] = new JsonArray(new JsonObject
            {
                ["type"] = 2,
                ["style"] = 1,
                ["label"] = "Reply to Santa",
                ["custom_id"] = $"ss_reply:{santaId}"
            })
        });
        await _rest.SendDmAsync(ulong.Parse(gifteeId),
            $"Your Secret Santa asks:\n\n{anonymized}", ct, components);
        return "Question sent anonymously.";
    }

    public async Task<string> ReplyToSantaAsync(ulong gifteeId, ulong santaId, string reply, CancellationToken ct)
    {
        var state = await _stateStore.LoadAsync(ct);
        var ev = state.CurrentEvent ?? throw new InvalidOperationException("No active event.");
        if (!ev.Communications.TryGetValue(santaId.ToString(), out var thread) ||
            thread.GifteeId != gifteeId.ToString())
            throw new InvalidOperationException("Invalid reply.");
        var anonymized = await AnonymizeAsync(reply, ct);
        thread.Thread.Add(new CommunicationMessage
        {
            From = "giftee",
            Text = anonymized,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
        await _stateStore.SaveAsync(state, ct);
        await _rest.SendDmAsync(santaId, $"Your giftee replied:\n\n{anonymized}", ct);
        return "Reply sent.";
    }

    public async Task WishlistAsync(ulong userId, string action, string? item, CancellationToken ct)
    {
        var state = await _stateStore.LoadAsync(ct);
        var ev = state.CurrentEvent ?? throw new InvalidOperationException("No active event.");
        if (!IsParticipant(userId)) throw new InvalidOperationException("Not a participant.");
        var key = userId.ToString();
        if (!ev.Wishlists.ContainsKey(key))
            ev.Wishlists[key] = [];

        switch (action.ToLowerInvariant())
        {
            case "add":
                if (string.IsNullOrWhiteSpace(item)) throw new InvalidOperationException("Item required.");
                ev.Wishlists[key].Add(item.Trim());
                break;
            case "remove":
                if (!int.TryParse(item, out var idx) || idx < 1 || idx > ev.Wishlists[key].Count)
                    throw new InvalidOperationException("Invalid item number.");
                ev.Wishlists[key].RemoveAt(idx - 1);
                break;
            case "clear":
                ev.Wishlists[key].Clear();
                break;
            case "view":
                break;
            default:
                throw new InvalidOperationException("Unknown action.");
        }
        await _stateStore.SaveAsync(state, ct);
    }

    public string FormatWishlist(IReadOnlyList<string> items) =>
        items.Count == 0 ? "*(empty)*" : string.Join("\n", items.Select((x, i) => $"{i + 1}. {x}"));

    public async Task SubmitGiftAsync(ulong userId, string gift, CancellationToken ct)
    {
        var state = await _stateStore.LoadAsync(ct);
        if (state.CurrentEvent is { } ev)
        {
            if (!ev.Assignments.TryGetValue(userId.ToString(), out var receiverId))
                throw new InvalidOperationException("No assignment.");
            ev.GiftSubmissions[userId.ToString()] = new GiftSubmission
            {
                Gift = gift,
                ReceiverId = receiverId,
                ReceiverName = ev.Participants.GetValueOrDefault(receiverId, "?"),
                SubmittedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            await _stateStore.SaveAsync(state, ct);
            return;
        }

        var year = state.CurrentEvent is null ? DateTime.UtcNow.Year : state.CurrentYear;
        var archive = _archiveStore.LoadArchive(year);
        if (archive is null)
            throw new InvalidOperationException("No active event or current-year archive.");
        if (!archive.Event.Assignments.TryGetValue(userId.ToString(), out var archivedReceiver))
            throw new InvalidOperationException("No assignment in archive.");
        archive.Event.GiftSubmissions[userId.ToString()] = new GiftSubmission
        {
            Gift = gift,
            ReceiverId = archivedReceiver,
            ReceiverName = archive.Event.Participants.GetValueOrDefault(archivedReceiver, "?"),
            SubmittedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        await _archiveStore.SaveArchiveAsync(archive, ct);
    }

    public async Task EditGiftAsync(ulong userId, int year, string giftDescription, CancellationToken ct)
    {
        var archive = _archiveStore.LoadArchive(year)
            ?? throw new FileNotFoundException($"No archive for {year}.");
        var key = userId.ToString();
        if (!archive.Event.Assignments.ContainsKey(key))
            throw new InvalidOperationException($"You did not participate in {year}.");
        var receiverId = archive.Event.Assignments[key];
        archive.Event.GiftSubmissions[key] = new GiftSubmission
        {
            Gift = giftDescription,
            ReceiverId = receiverId,
            ReceiverName = archive.Event.Participants.GetValueOrDefault(receiverId, "?"),
            SubmittedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        await _archiveStore.SaveArchiveAsync(archive, ct);
    }

    public async Task ArchiveDeleteAsync(int year, CancellationToken ct)
    {
        var state = await _stateStore.LoadAsync(ct);
        if (state.CurrentEvent is not null && state.CurrentYear == year)
            throw new InvalidOperationException("Cannot delete archive for active year. Run /ss stop first.");
        _archiveStore.MoveArchiveToBackup(year);
    }

    public Task ArchiveRestoreAsync(int year, CancellationToken ct)
    {
        _archiveStore.RestoreArchiveFromBackup(year);
        return Task.CompletedTask;
    }

    public string FormatArchiveBackups() =>
        string.Join(", ", _archiveStore.ListBackupYears().Select(y => y.ToString()));

    public string FormatHistorySummary()
    {
        var years = _archiveStore.ListArchiveYears();
        if (years.Count == 0) return "No archived years yet.";
        return string.Join("\n", years.Select(y =>
        {
            var a = _archiveStore.LoadArchive(y);
            return a is null ? $"- {y}" : $"- **{y}**: {a.Event.Participants.Count} participants, {a.Event.Assignments.Count} assignments";
        }));
    }

    public string FormatUserHistory(ulong userId)
    {
        var key = userId.ToString();
        var lines = new List<string>();
        foreach (var year in _archiveStore.ListArchiveYears())
        {
            var a = _archiveStore.LoadArchive(year);
            if (a is null) continue;
            if (!a.Event.Participants.ContainsKey(key)) continue;
            var gift = a.Event.GiftSubmissions.GetValueOrDefault(key)?.Gift ?? "(no gift logged)";
            var gaveTo = a.Event.Assignments.TryGetValue(key, out var r)
                ? a.Event.Participants.GetValueOrDefault(r, r) : "?";
            lines.Add($"**{year}** — gave to {gaveTo}: {gift}");
        }
        return lines.Count == 0 ? "No history found for this user." : string.Join("\n", lines);
    }

    public string FormatOversight(string view)
    {
        var state = _stateStore.LoadAsync().GetAwaiter().GetResult();
        var ev = state.CurrentEvent;
        if (ev is null) return "No active event.";

        view = view.ToLowerInvariant();
        if (view is "gifts" or "all")
        {
            var gifts = string.Join("\n", ev.GiftSubmissions.Select(kv =>
                $"- <@{kv.Key}> → {kv.Value.Gift}"));
            if (view == "gifts") return $"**Gifts**\n{gifts}";
            var comms = string.Join("\n", ev.Communications.Select(kv =>
                $"- Santa <@{kv.Key}> ↔ Giftee <@{kv.Value.GifteeId}>: {kv.Value.Thread.Count} messages"));
            return $"**Gifts**\n{gifts}\n\n**Comms**\n{comms}";
        }

        if (view == "comms")
        {
            return string.Join("\n", ev.Communications.Select(kv =>
                $"- Santa <@{kv.Key}> ↔ Giftee <@{kv.Value.GifteeId}>: {kv.Value.Thread.Count} messages"));
        }
        return "Use view: gifts, comms, or all.";
    }

    private async Task<string> AnonymizeAsync(string text, CancellationToken ct)
    {
        var payload = new
        {
            model = "gpt-3.5-turbo",
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = "Rewrite to remove identifying info but keep meaning. Return only rewritten text:\n\n" + text
                }
            },
            max_tokens = 300,
            temperature = 0.1
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.OpenAiApiKey);
        request.Content = JsonContent.Create(payload);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return text;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim() ?? text;
    }
}
