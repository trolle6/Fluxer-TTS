using System.Text.RegularExpressions;

namespace WaveTechFluxerTTS.Tts;

public sealed partial class TextProcessor
{
    private static readonly (string Pattern, string Replacement)[] Corrections =
    [
        (@"\bim\b", "I'm"),
        (@"\byoure\b", "you're"),
        (@"\bdont\b", "don't"),
        (@"\bcant\b", "can't"),
        (@"\bwont\b", "won't"),
        (@"\btheyre\b", "they're"),
        (@"\bweve\b", "we've"),
        (@"\bive\b", "I've"),
    ];

    [GeneratedRegex(@"<(a?):([\w-]+):\d+>")]
    private static partial Regex EmojiPattern();

    [GeneratedRegex(@"<@!?\d+>|<@&\d+>|<#\d+>|https?://\S+")]
    private static partial Regex DiscordCleanupPattern();

    [GeneratedRegex(@"\b[A-Z]{2,4}\b|\b[a-z]+[A-Z]+[a-z]*\b|\b[A-Z]+[a-z]+[A-Z]+\b|\b[A-Za-z]+\d+\b|\b\d+[A-Za-z]+\b", RegexOptions.IgnoreCase)]
    private static partial Regex PronunciationPattern();

    public string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        text = Regex.Replace(text, @"-{3,}", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        text = EmojiPattern().Replace(text, m => m.Groups[2].Value);
        text = DiscordCleanupPattern().Replace(text, string.Empty);
        text = Regex.Replace(text, @"\s+", " ").Trim();

        foreach (var (pattern, replacement) in Corrections)
            text = Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase);

        return text;
    }

    public bool NeedsPronunciationHelp(string text) => PronunciationPattern().IsMatch(text);

    public IReadOnlyList<string> SplitIntoChunks(string text, int maxChunkSize = 4000)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();
        if (text.Length <= maxChunkSize)
            return [text];

        var chunks = new List<string>();
        var remaining = text;
        while (remaining.Length > maxChunkSize)
        {
            var slice = remaining[..maxChunkSize];
            var breakAt = Math.Max(slice.LastIndexOf('.'), Math.Max(slice.LastIndexOf('!'), slice.LastIndexOf('?')));
            if (breakAt < (int)(maxChunkSize * 0.8))
                breakAt = maxChunkSize;
            else
                breakAt++;

            var chunk = remaining[..breakAt].Trim();
            if (chunk.Length >= 2)
                chunks.Add(chunk);
            remaining = remaining[breakAt..].TrimStart();
        }

        if (remaining.Length >= 2)
            chunks.Add(remaining);
        return chunks;
    }
}
