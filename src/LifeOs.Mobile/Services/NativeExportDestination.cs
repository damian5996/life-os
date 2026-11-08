using LifeOs.Api.Ui;

namespace LifeOs.Mobile.Services;

public sealed class NativeExportDestination : IExportDestination
{
    public async Task ShareAsync(ExportFile file)
    {
        // Each share gets its own app-private cache directory. Android may read it after
        // the share sheet closes, so leave cleanup to the cache lifecycle.
        var directory = Path.Combine(FileSystem.CacheDirectory, "sharing-root", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Path.GetFileName(file.FileName));
        await File.WriteAllBytesAsync(path, file.Content);
        await MainThread.InvokeOnMainThreadAsync(() => Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Eksport notatek Life OS",
            File = new ShareFile(path, file.ContentType)
        }));
    }
}
