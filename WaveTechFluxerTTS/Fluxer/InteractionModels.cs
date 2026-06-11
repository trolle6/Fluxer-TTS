using System.Text.Json;

namespace WaveTechFluxerTTS.Fluxer;

public sealed class InteractionContext
{
    public required string InteractionId { get; init; }
    public required string InteractionToken { get; init; }
    public required int Type { get; init; }
    public ulong? GuildId { get; init; }
    public ulong? ChannelId { get; init; }
    public ulong UserId { get; init; }
    public JsonElement Data { get; init; }
    public JsonElement Root { get; init; }
    public JsonElement? Member { get; init; }
    public string? CommandName { get; init; }
    public IReadOnlyList<InteractionOption> Options { get; init; } = [];
    public string? CustomId { get; init; }
}

public readonly record struct ResolvedAttachment(string Id, string Filename, string Url, int Size);

public readonly record struct InteractionOption(string Name, int Type, string? StringValue, long? IntValue, bool? BoolValue);

public static class InteractionTypes
{
    public const int Ping = 1;
    public const int ApplicationCommand = 2;
    public const int MessageComponent = 3;
    public const int Autocomplete = 4;
    public const int ModalSubmit = 5;
}

public static class InteractionResponseType
{
    public const int ChannelMessageWithSource = 4;
    public const int DeferredChannelMessageWithSource = 5;
    public const int DeferredUpdateMessage = 6;
    public const int UpdateMessage = 7;
    public const int AutocompleteResult = 8;
    public const int Modal = 9;
}
