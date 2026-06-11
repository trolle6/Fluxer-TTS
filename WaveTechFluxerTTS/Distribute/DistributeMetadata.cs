using System.Text.Json.Serialization;

namespace WaveTechFluxerTTS.Distribute;

public sealed class DistributeMetadata
{
    [JsonPropertyName("files")]
    public Dictionary<string, DistributedFileEntry> Files { get; set; } = new();

    [JsonPropertyName("history")]
    public List<DistributeHistoryEntry> History { get; set; } = new();
}

public sealed class DistributedFileEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = "";

    [JsonPropertyName("uploaded_by")]
    public ulong UploadedBy { get; set; }

    [JsonPropertyName("required_by")]
    public ulong RequiredBy { get; set; }

    [JsonPropertyName("uploaded_at")]
    public double UploadedAt { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("download_count")]
    public int DownloadCount { get; set; }
}

public sealed class DistributeHistoryEntry
{
    [JsonPropertyName("file_id")]
    public string FileId { get; set; } = "";

    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}
