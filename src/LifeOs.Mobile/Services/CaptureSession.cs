namespace LifeOs.Mobile.Services;

// Singleton owns workflow state across page/activity recreation; no upload queue or database.
public sealed class CaptureSession
{
    private const string PendingKey = "pending-audio";
    private readonly IAudioRecorder recorder;
    private readonly ILifeOsApiClient api;
    public event Action? Changed;
    public bool Busy { get; private set; }
    public bool IsRecording => recorder.IsRecording;
    public TimeSpan Elapsed => recorder.Elapsed;
    public string Status { get; private set; } = "Gotowe do nagrania";
    public string? PendingPath { get; private set; }
    public bool CanRetry => !Busy && !IsRecording && PendingPath is not null;

    public CaptureSession(IAudioRecorder recorder, ILifeOsApiClient api)
    {
        this.recorder = recorder;
        this.api = api;
        var pending = Preferences.Default.Get(PendingKey, "");
        if (File.Exists(pending))
        {
            PendingPath = pending;
            Status = "Pozostało niewysłane nagranie. Po przerwaniu aplikacji plik może być niepełny. " +
                "Przed ponowieniem sprawdź, czy notatka jest już na serwerze.";
        }
        recorder.Interrupted += message => MainThread.BeginInvokeOnMainThread(() =>
        {
            Status = message;
            Changed?.Invoke();
        });
    }

    public async Task StartAsync()
    {
        if (Busy || IsRecording || PendingPath is not null) return;
        Busy = true;
        Changed?.Invoke();
        try
        {
            var folder = Path.Combine(FileSystem.AppDataDirectory, "recordings");
            Directory.CreateDirectory(folder);
            PendingPath = Path.Combine(folder, $"{Guid.NewGuid():N}.m4a");
            Preferences.Default.Set(PendingKey, PendingPath);
            await recorder.StartAsync(PendingPath);
            Status = "Nagrywanie — możesz robić przerwy w mówieniu";
        }
        catch
        {
            Status = "Nie udało się rozpocząć nagrywania. Sprawdź dostęp do mikrofonu.";
            if (!File.Exists(PendingPath)) ClearPending();
        }
        finally { Busy = false; Changed?.Invoke(); }
    }

    public async Task StopAndUploadAsync()
    {
        if (Busy || !IsRecording) return;
        Busy = true;
        Status = "Kończenie nagrania...";
        Changed?.Invoke();
        try
        {
            await recorder.StopAsync();
            await UploadAsync();
        }
        catch (Exception e) { SetFailure(e); }
        finally { Busy = false; Changed?.Invoke(); }
    }

    public async Task RetryAsync()
    {
        if (!CanRetry) return;
        Busy = true;
        Changed?.Invoke();
        try { await UploadAsync(); }
        catch (Exception e) { SetFailure(e); }
        finally { Busy = false; Changed?.Invoke(); }
    }

    public void KeepFileAndStartFresh()
    {
        if (!CanRetry) return;
        // Keep the old private file. Only release the single pending UI slot.
        ClearPending();
        Status = "Poprzedni plik pozostał na telefonie. Gotowe do nowego nagrania.";
        Changed?.Invoke();
    }

    private async Task UploadAsync()
    {
        Status = "Wysyłanie...";
        Changed?.Invoke();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(11));
        var result = await api.UploadAudioAsync(PendingPath!, () => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!Busy) return;
            Status = "Transkrypcja i analiza...";
            Changed?.Invoke();
        }), timeout.Token);
        Status = result.ProcessingState == "COMPLETED" ? "✓ Zapisano" : "✓ Zapisano — analiza AI nie powiodła się. Oryginał jest na serwerze.";
        try { File.Delete(PendingPath!); }
        catch (IOException) { Status += " Nie udało się usunąć lokalnej kopii."; }
        catch (UnauthorizedAccessException) { Status += " Nie udało się usunąć lokalnej kopii."; }
        ClearPending();
    }

    private void ClearPending() { PendingPath = null; Preferences.Default.Remove(PendingKey); }
    private void SetFailure(Exception error)
    {
        Status = "Nie udało się zapisać notatki. Nagranie pozostało na telefonie. " +
            (error is InvalidOperationException ? error.Message :
                "Sprawdź połączenie i zapis na serwerze przed ponowieniem, aby uniknąć duplikatu.");
    }
}
