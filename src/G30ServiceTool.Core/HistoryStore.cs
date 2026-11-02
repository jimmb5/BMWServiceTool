using System.Text.Json;
using System.Text.Json.Serialization;

namespace G30ServiceTool.Core;

public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "history.json");
    public string BackupDirectory => Path.Combine(DirectoryPath, "backups");

    public HistoryStore(string directoryPath) => DirectoryPath = Path.GetFullPath(directoryPath);

    public static HistoryStore ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "G30ServiceTool"));

    public ServiceHistory Load()
    {
        if (!File.Exists(FilePath)) return new ServiceHistory();
        return Read(FilePath);
    }

    public static ServiceHistory Read(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 10 * 1024 * 1024)
                throw new ValidationException("Huoltokirjatiedosto on liian suuri (enintään 10 Mt).");
            var history = JsonSerializer.Deserialize<ServiceHistory>(File.ReadAllText(path), JsonOptions)
                ?? throw new ValidationException("Huoltokirjatiedosto on tyhjä.");
            EntryValidation.ValidateHistory(history);
            return history;
        }
        catch (JsonException ex)
        {
            throw new ValidationException("Huoltokirjatiedoston JSON-rakenne on virheellinen. Alkuperäistä ei muutettu. " + ex.Message);
        }
    }

    // Acquire before reading as well as writing: two app instances cannot lose each other's entries.
    public ServiceHistory Add(ServiceEntry entry)
    {
        EntryValidation.Validate(entry);
        using var fileLock = AcquireLock();
        var current = Load(); // Never overwrite a malformed existing history.
        var next = current with { Entries = [.. current.Entries, entry] };
        EntryValidation.ValidateHistory(next);
        SaveWithBackup(next);
        return next;
    }

    public ServiceHistory MergeBackup(string path)
    {
        var imported = Read(path);
        using var fileLock = AcquireLock();
        var current = Load();
        var merged = current.Entries.ToDictionary(e => e.Id);
        foreach (var entry in imported.Entries)
        {
            if (merged.TryGetValue(entry.Id, out var existing) && SerializeEntry(existing) != SerializeEntry(entry))
                throw new ValidationException("Varmuuskopiossa on ristiriitainen merkintä samalla tunnisteella. Mitään ei muutettu.");
            merged[entry.Id] = entry;
        }
        var next = current with { Entries = merged.Values.ToList() };
        EntryValidation.ValidateHistory(next);
        SaveWithBackup(next);
        return next;
    }

    public void ExportBackup(string destination)
    {
        var target = Path.GetFullPath(destination);
        if (string.Equals(target, FilePath, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Valitse varmuuskopiolle toinen tiedosto kuin käytössä oleva huoltokirja.");
        using var fileLock = AcquireLock();
        WriteAtomic(target, JsonSerializer.Serialize(Load(), JsonOptions));
    }

    private FileStream AcquireLock()
    {
        Directory.CreateDirectory(DirectoryPath);
        try { return new FileStream(Path.Combine(DirectoryPath, ".history.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new IOException("Huoltokirja on toisen toiminnon käytössä. Yritä uudelleen."); }
    }

    private void SaveWithBackup(ServiceHistory history)
    {
        if (File.Exists(FilePath))
        {
            Directory.CreateDirectory(BackupDirectory);
            var backup = Path.Combine(BackupDirectory, $"history-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.json");
            File.Copy(FilePath, backup, overwrite: false); // Backup failure aborts the write.
        }
        WriteAtomic(FilePath, JsonSerializer.Serialize(history, JsonOptions));
    }

    private static string SerializeEntry(ServiceEntry entry) => JsonSerializer.Serialize(entry, JsonOptions);

    private static void WriteAtomic(string target, string content)
    {
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, target, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
