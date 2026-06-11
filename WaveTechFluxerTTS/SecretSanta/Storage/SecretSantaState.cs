using System.Text.Json.Serialization;

namespace WaveTechFluxerTTS.SecretSanta.Storage;

public sealed class SecretSantaState
{
    [JsonPropertyName("current_year")]
    public int CurrentYear { get; set; } = DateTime.UtcNow.Year;

    [JsonPropertyName("pair_history")]
    public Dictionary<string, List<long>> PairHistory { get; set; } = new();

    [JsonPropertyName("current_event")]
    public SecretSantaEvent? CurrentEvent { get; set; }
}

public sealed class SecretSantaEvent
{
    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;

    [JsonPropertyName("guild_id")]
    public ulong GuildId { get; set; }

    [JsonPropertyName("announcement_message_id")]
    public ulong AnnouncementMessageId { get; set; }

    [JsonPropertyName("announcement_channel_id")]
    public ulong AnnouncementChannelId { get; set; }

    [JsonPropertyName("role_id")]
    public ulong? RoleId { get; set; }

    [JsonPropertyName("join_closed")]
    public bool JoinClosed { get; set; }

    [JsonPropertyName("participants")]
    public Dictionary<string, string> Participants { get; set; } = new();

    [JsonPropertyName("assignments")]
    public Dictionary<string, string> Assignments { get; set; } = new();

    [JsonPropertyName("wishlists")]
    public Dictionary<string, List<string>> Wishlists { get; set; } = new();

    [JsonPropertyName("gift_submissions")]
    public Dictionary<string, GiftSubmission> GiftSubmissions { get; set; } = new();

    [JsonPropertyName("communications")]
    public Dictionary<string, CommunicationThread> Communications { get; set; } = new();
}

public sealed class GiftSubmission
{
    [JsonPropertyName("gift")]
    public string Gift { get; set; } = "";

    [JsonPropertyName("receiver_id")]
    public string ReceiverId { get; set; } = "";

    [JsonPropertyName("receiver_name")]
    public string ReceiverName { get; set; } = "";

    [JsonPropertyName("submitted_at")]
    public double SubmittedAt { get; set; }
}

public sealed class CommunicationThread
{
    [JsonPropertyName("giftee_id")]
    public string GifteeId { get; set; } = "";

    [JsonPropertyName("thread")]
    public List<CommunicationMessage> Thread { get; set; } = new();
}

public sealed class CommunicationMessage
{
    [JsonPropertyName("from")]
    public string From { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public double Timestamp { get; set; }
}

public sealed class YearArchive
{
    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("archived_at")]
    public double ArchivedAt { get; set; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("event")]
    public SecretSantaEvent Event { get; set; } = new();
}
