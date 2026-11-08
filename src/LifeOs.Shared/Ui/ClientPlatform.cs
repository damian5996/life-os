namespace LifeOs.Api.Ui;

public sealed record LifeOsClientOptions(Uri BaseAddress);

public sealed record ExportFile(byte[] Content, string FileName, string ContentType);

// The web uses ordinary download links; native hosts provide file sharing.
public interface IExportDestination
{
    Task ShareAsync(ExportFile file);
}

public sealed class CaptureChanges
{
    public event Action? Changed;
    public void Notify() => Changed?.Invoke();
}
