using System.Text.Json;
using System.Text.Json.Serialization;

namespace LifeOs.Mobile.Services;

public sealed record QueuedRecording(Guid Id, DateTimeOffset CapturedAt, string State, string? Error = null);

// Persist confirmation before cleanup, so a crash cannot requeue a confirmed upload.
public sealed class RecordingQueue
{
    private readonly string folder;
    private readonly Dictionary<Guid, QueuedRecording> entries = [];
    public IReadOnlyList<QueuedRecording> Pending => entries.Values.Where(x => x.State != "synced").OrderBy(x => x.CapturedAt).ToArray();
    public RecordingQueue(string folder)
    {
        this.folder = folder;
        Directory.CreateDirectory(folder);
        foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                var item = JsonSerializer.Deserialize(File.ReadAllText(path), QueueJsonContext.Default.QueuedRecording);
                if (item is not null && Path.GetFileNameWithoutExtension(path) == item.Id.ToString("N")) entries[item.Id] = item;
            }
            catch (JsonException) { /* Recover audio below; do not delete unacknowledged files. */ }
        }
        foreach (var item in entries.Values.ToArray())
        {
            if (item.State == "synced") { Cleanup(item); continue; }
            if (item.State == "recording") Save(item with { State = "ready", Error = "Nagrywanie zostało przerwane. Plik może być niepełny." });
        }
        // Migrate retained files from the former single-pending-recording workflow.
        foreach (var path in Directory.EnumerateFiles(folder, "*.m4a"))
            if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id) && !entries.ContainsKey(id))
                Save(new(id, new DateTimeOffset(File.GetCreationTimeUtc(path)), "ready", "Odzyskane nagranie; data pochodzi z utworzenia pliku."));
    }
    public string AudioPath(QueuedRecording item) => Path.Combine(folder, item.Id.ToString("N") + ".m4a");
    public QueuedRecording Begin(DateTimeOffset now)
    {
        var item = new QueuedRecording(Guid.NewGuid(), now.ToUniversalTime(), "recording");
        Save(item);
        return item;
    }
    public void Ready(QueuedRecording item, string? error = null) => Save(item with { State = "ready", Error = error });
    public void Complete(QueuedRecording item)
    {
        var synced = item with { State = "synced", Error = null };
        Save(synced);
        Cleanup(synced);
    }
    public void Remove(QueuedRecording item)
    {
        File.Delete(AudioPath(item));
        File.Delete(Manifest(item));
        entries.Remove(item.Id);
    }
    private string Manifest(QueuedRecording item) => Path.Combine(folder, item.Id.ToString("N") + ".json");
    private void Save(QueuedRecording item)
    {
        var path = Manifest(item);
        using (var file = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, item, QueueJsonContext.Default.QueuedRecording);
            file.Flush(flushToDisk: true);
        }
        File.Move(path + ".tmp", path, overwrite: true);
        entries[item.Id] = item;
    }
    private void Cleanup(QueuedRecording item)
    {
        try { Remove(item); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

[JsonSerializable(typeof(QueuedRecording))]
internal partial class QueueJsonContext : JsonSerializerContext;
