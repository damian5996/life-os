namespace LifeOs.Mobile.Services;

public interface IAudioRecorder
{
    bool IsRecording { get; }
    TimeSpan Elapsed { get; }
    event Action<string>? Interrupted;
    Task StartAsync(string path);
    Task StopAsync();
}
