using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LifeOs.Mobile.Models;

namespace LifeOs.Mobile.Services;

public interface ILifeOsApiClient
{
    Task<CaptureResponse> UploadAudioAsync(string path, Action uploaded, CancellationToken cancellationToken);
}

public sealed class LifeOsApiClient(HttpClient http, ApiConfiguration config) : ILifeOsApiClient
{
    public async Task<CaptureResponse> UploadAudioAsync(string path, Action uploaded, CancellationToken cancellationToken)
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
        var audio = new UploadContent(stream, uploaded);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/mp4");
        form.Add(audio, "file", "capture.m4a");
        using var response = await http.PostAsync(new Uri(baseUrl.AbsoluteUri.TrimEnd('/') + "/api/captures/audio"), form, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Created)
            throw new HttpRequestException("Nie udało się zapisać notatki. Nagranie zostało na telefonie. " +
                "Przed ponowieniem sprawdź zapis na serwerze — mógł zakończyć się mimo błędu połączenia.");
        var capture = await response.Content.ReadFromJsonAsync(MobileJsonContext.Default.CaptureResponse, cancellationToken);
        if (capture is null || capture.Id == Guid.Empty || capture.ProcessingState is not ("COMPLETED" or "FAILED"))
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
