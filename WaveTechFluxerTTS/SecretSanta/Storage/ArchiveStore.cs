using WaveTechFluxerTTS.Bot;
using WaveTechFluxerTTS.Utils;

namespace WaveTechFluxerTTS.SecretSanta.Storage;

public sealed class ArchiveStore
{
    private readonly string _archiveDir;
    private readonly string _backupsDir;

    public ArchiveStore(BotConfig config)
    {
        _archiveDir = config.ArchiveDir;
        _backupsDir = config.ArchiveBackupsDir;
    }

    public string GetArchivePath(int year) => Path.Combine(_archiveDir, $"{year}.json");

    public async Task SaveArchiveAsync(YearArchive archive, CancellationToken ct = default)
    {
        var path = GetArchivePath(archive.Year);
        if (File.Exists(path))
        {
            var backupName = $"{archive.Year}_backup_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.json";
            File.Copy(path, Path.Combine(_backupsDir, backupName), overwrite: false);
        }
        await AtomicJsonFile.SaveAsync(path, archive, ct);
    }

    public YearArchive? LoadArchive(int year)
    {
        var path = GetArchivePath(year);
        return File.Exists(path) ? AtomicJsonFile.Load<YearArchive>(path, null!) : null;
    }

    public void MoveArchiveToBackup(int year)
    {
        var path = GetArchivePath(year);
        if (!File.Exists(path))
            throw new FileNotFoundException($"No archive for {year}");
        var backupPath = Path.Combine(_backupsDir, $"{year}.json");
        if (File.Exists(backupPath))
            throw new InvalidOperationException($"Backup for {year} already exists.");
        File.Move(path, backupPath);
    }

    public void RestoreArchiveFromBackup(int year)
    {
        var backupPath = Path.Combine(_backupsDir, $"{year}.json");
        if (!File.Exists(backupPath))
            throw new FileNotFoundException($"No backup for {year}");
        var path = GetArchivePath(year);
        if (File.Exists(path))
            throw new InvalidOperationException($"Archive for {year} already exists.");
        File.Move(backupPath, path);
    }

    public IReadOnlyList<int> ListBackupYears()
    {
        return Directory.GetFiles(_backupsDir, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(n => int.TryParse(n, out _))
            .Select(int.Parse)
            .OrderByDescending(y => y)
            .ToList();
    }

    public IReadOnlyList<int> ListArchiveYears()
    {
        return Directory.GetFiles(_archiveDir, "[0-9][0-9][0-9][0-9].json")
            .Select(f => int.Parse(Path.GetFileNameWithoutExtension(f)))
            .OrderByDescending(y => y)
            .ToList();
    }

    public Dictionary<string, List<long>> LoadPairHistoryFromArchives()
    {
        var history = new Dictionary<string, List<long>>();
        foreach (var file in Directory.GetFiles(_archiveDir, "[0-9][0-9][0-9][0-9].json"))
        {
            var archive = AtomicJsonFile.Load<YearArchive>(file, null!);
            if (archive?.Event.Assignments is null) continue;
            foreach (var (giver, receiver) in archive.Event.Assignments)
            {
                if (long.TryParse(receiver, out var receiverId))
                {
                    if (!history.ContainsKey(giver))
                        history[giver] = [];
                    history[giver].Add(receiverId);
                }
            }
        }
        return history;
    }
}
