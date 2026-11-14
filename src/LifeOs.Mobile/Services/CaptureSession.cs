namespace LifeOs.Mobile.Services;

public sealed class CaptureSession
{
    private readonly IAudioRecorder recorder;
    private readonly ILifeOsApiClient api;
    private readonly RecordingQueue queue;
    private readonly Func<bool> online;
    private readonly Action<Action> dispatch;
    private readonly TimeProvider clock;
    private QueuedRecording? active;
    public event Action? Changed;
    public bool Busy { get; private set; }
    public bool IsRecording => recorder.IsRecording;
    public TimeSpan Elapsed => recorder.Elapsed;
    public bool IsOnline => online();
    public IReadOnlyList<QueuedRecording> Pending => queue.Pending.Where(x => x.Id != active?.Id).ToArray();
    public bool CanRetry => !Busy && !IsRecording && Pending.Count > 0;
    public string Status { get; private set; } = "Gotowe do nagrania — także bez internetu";

    public CaptureSession(IAudioRecorder recorder, ILifeOsApiClient api, RecordingQueue queue,
        Func<bool> online, Action<Action> dispatch, TimeProvider clock)
    {
        this.recorder = recorder; this.api = api; this.queue = queue;
        this.online = online; this.dispatch = dispatch; this.clock = clock;
        recorder.Interrupted += message => dispatch(() =>
        {
            if (active is not null) { queue.Ready(active, message); active = null; }
            Status = message; Changed?.Invoke();
        });
    }
    public async Task StartAsync()
    {
        if (Busy || IsRecording) return;
        Busy = true; Changed?.Invoke();
        try
        {
            active = queue.Begin(clock.GetUtcNow());
            await recorder.StartAsync(queue.AudioPath(active));
            Status = "Nagrywanie — możesz robić przerwy w mówieniu";
        }
        catch
        {
            Status = "Nie udało się rozpocząć nagrywania. Sprawdź dostęp do mikrofonu.";
            if (active is not null)
            {
                if (File.Exists(queue.AudioPath(active))) queue.Ready(active, Status);
                else queue.Remove(active);
                active = null;
            }
        }
        finally { Busy = false; Changed?.Invoke(); }
    }
    public async Task StopAndUploadAsync()
    {
        if (Busy || !IsRecording || active is null) return;
        Busy = true; Status = "Kończenie nagrania..."; Changed?.Invoke();
        var item = active;
        try
        {
            await recorder.StopAsync();
            queue.Ready(item); active = null;
            if (IsOnline) await UploadAsync(item);
            else Status = "Zapisano na telefonie. Po połączeniu z internetem naciśnij „Synchronizuj”.";
        }
        catch (Exception error) { Failure(item, error); active = null; }
        finally { Busy = false; Changed?.Invoke(); }
    }
    public async Task RetryAsync()
    {
        if (!CanRetry) return;
        if (!IsOnline) { Status = "Brak internetu. Nagrania czekają na telefonie."; Changed?.Invoke(); return; }
        Busy = true; Changed?.Invoke();
        try
        {
            foreach (var item in Pending.ToArray())
            {
                if (!IsOnline) break;
                try { await UploadAsync(item); }
                catch (Exception error) { Failure(item, error); }
            }
            Status = Pending.Count == 0 ? "✓ Wszystkie nagrania zsynchronizowane" :
                $"Pozostało nagrań: {Pending.Count}. Szczegóły poniżej. Naciśnij „Synchronizuj”, aby ponowić.";
        }
        finally { Busy = false; Changed?.Invoke(); }
    }
    public void Remove(QueuedRecording item)
    {
        if (Busy || IsRecording) return;
        try { queue.Remove(item); Status = "Usunięto lokalne nagranie."; }
        catch (IOException) { Status = "Nie udało się usunąć pliku."; }
        Changed?.Invoke();
    }
    private async Task UploadAsync(QueuedRecording item)
    {
        Status = "Wysyłanie..."; Changed?.Invoke();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(11));
        var result = await api.UploadAudioAsync(queue.AudioPath(item), item.Id, item.CapturedAt, () => dispatch(() =>
        { Status = "Transkrypcja i analiza..."; Changed?.Invoke(); }), timeout.Token);
        queue.Complete(item);
        Status = result.ProcessingState switch
        {
            "COMPLETED" => "✓ Zapisano",
            "PENDING" => "✓ Zapisano — analiza jest w toku",
            _ => "✓ Zapisano — analiza AI nie powiodła się. Oryginał jest na serwerze."
        };
    }
    private void Failure(QueuedRecording item, Exception error)
    {
        Status = error is InvalidOperationException or HttpRequestException { StatusCode: not null } ? error.Message :
            "Nie udało się wysłać nagrania. Plik pozostał na telefonie. Ponów synchronizację po sprawdzeniu połączenia.";
        queue.Ready(item, Status); Changed?.Invoke();
    }
}
