using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LifeOs.Mobile.Models;

namespace LifeOs.Mobile.Services;

public interface ILifeOsApiClient
{
    Task<CaptureResponse> UploadAudioAsync(string path, Action uploaded, CancellationToken cancellationToken);
    Task<CaptureResponse> UploadAudioAsync(string path, Guid recordingId, DateTimeOffset capturedAt, Action uploaded, CancellationToken cancellationToken);
}

public sealed class NoSpeechDetectedException() : InvalidOperationException(
    "Nie wykryto mowy. Notatka nie została utworzona. Nagranie pozostało na telefonie. " +
    "Możesz nagrać nową notatkę lub ponowić synchronizację.");

public sealed class LifeOsApiClient(HttpClient http, ApiConfiguration config) : ILifeOsApiClient
{
    public Task<CaptureResponse> UploadAudioAsync(string path, Action uploaded, CancellationToken cancellationToken) =>
        UploadCoreAsync(path, null, null, uploaded, cancellationToken);
    public Task<CaptureResponse> UploadAudioAsync(string path, Guid recordingId, DateTimeOffset capturedAt, Action uploaded, CancellationToken cancellationToken) =>
        UploadCoreAsync(path, recordingId, capturedAt, uploaded, cancellationToken);
    private async Task<CaptureResponse> UploadCoreAsync(string path, Guid? recordingId, DateTimeOffset? capturedAt, Action uploaded, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(config.ApiBaseUrl, UriKind.Absolute, out var baseUrl) ||
            baseUrl.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Nieprawidłowy adres API w konfiguracji aplikacji.");
#if !DEBUG
        if (baseUrl.Scheme != "https") throw new InvalidOperationException("Wersja Release wymaga adresu HTTPS.");
#endif
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > 25_000_000)
            throw new InvalidOperationException("Nagranie musi mieć od 1 bajta do 25 MB. Plik pozostaje na telefonie.");
        await using var stream = File.OpenRead(path);
        using var form = new MultipartFormDataContent();
        if (recordingId.HasValue)
        {
            form.Add(new StringContent(recordingId.Value.ToString("D")), "recordingId");
            form.Add(new StringContent(capturedAt!.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture)), "capturedAt");
        }
        var audio = new UploadContent(stream, uploaded);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/mp4");
        form.Add(audio, "file", "capture.m4a");
        using var response = await http.PostAsync(new Uri(baseUrl.AbsoluteUri.TrimEnd('/') + "/api/captures/audio"), form, cancellationToken);
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            throw new NoSpeechDetectedException();
        if (response.StatusCode != HttpStatusCode.Created)
            throw new HttpRequestException("Nie udało się zapisać notatki. Nagranie zostało na telefonie. " +
                "Przed ponowieniem sprawdź zapis na serwerze — mógł zakończyć się mimo błędu połączenia.");
        var capture = await response.Content.ReadFromJsonAsync(MobileJsonContext.Default.CaptureResponse, cancellationToken);
        if (capture is null || capture.Id == Guid.Empty || (recordingId.HasValue && capture.Id != recordingId) ||
            (capture.ProcessingState is not ("COMPLETED" or "FAILED") && !(recordingId.HasValue && capture.ProcessingState == "PENDING")))
            throw new HttpRequestException("Nieprawidłowe potwierdzenie zapisu. Nagranie pozostało na telefonie.");
        return capture;
    }

    // Switch UI phase when the file bytes have been written to the HTTP transport.
    // The synchronous backend does not expose separate transcription/classification progress.
    private sealed class UploadContent(Stream stream, Action uploaded) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = stream.Length; return true; }
        protected override Task SerializeToStreamAsync(Stream target, TransportContext? context) => Copy(target, default);
        protected override Task SerializeToStreamAsync(Stream target, TransportContext? context, CancellationToken cancellationToken) => Copy(target, cancellationToken);
        private async Task Copy(Stream target, CancellationToken cancellationToken)
        {
            await stream.CopyToAsync(target, cancellationToken);
            uploaded();
        }
    }
}
