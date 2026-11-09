using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LifeOs.Api.Models;
using Microsoft.AspNetCore.Components;

namespace LifeOs.Api.Ui;

public sealed class LifeOsApiClient(IHttpClientFactory factory, NavigationManager navigation, LifeOsClientOptions? options = null)
{
    private Uri ApiUri(string path) => options is null ? navigation.ToAbsoluteUri(path)
        : new Uri(new Uri(options.BaseAddress.AbsoluteUri.TrimEnd('/') + "/"), path);

    public Task<Capture[]> GetCapturesAsync(CaptureType? type, bool deleted = false) => SendAsync<Capture[]>(HttpMethod.Get,
        "api/captures?limit=100" + (type is null ? "" : $"&type={type}") + (deleted ? "&deleted=true" : ""), ClientJsonContext.Default.CaptureArray);

    public Task<Capture> CreateCaptureAsync(string text) => SendAsync<Capture>(HttpMethod.Post,
        "api/captures", ClientJsonContext.Default.Capture,
        JsonContent.Create(new TextCaptureInput(text, "TEXT"), ClientJsonContext.Default.TextCaptureInput));

    public Task DeleteCaptureAsync(Guid id) => SendAsync<object>(HttpMethod.Delete, $"api/captures/{id}", noContent: true);
    public Task RestoreCaptureAsync(Guid id) => SendAsync<object>(HttpMethod.Post, $"api/captures/{id}/restore", noContent: true);

    public Task<WeeklyReview> GenerateReviewAsync(string period = "week", DateOnly? from = null, DateOnly? to = null) => SendAsync<WeeklyReview>(HttpMethod.Post,
        "api/review?period=" + Uri.EscapeDataString(period) + (period == "custom" ? $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}" : ""), ClientJsonContext.Default.WeeklyReview);

    public async Task<ExportFile> DownloadExportAsync(string format, CaptureType? type, bool deleted)
    {
        if (format is not ("markdown" or "json")) throw new UiApiException("Nieobsługiwany format eksportu.");
        try
        {
            using var client = factory.CreateClient("LifeOsUi");
            using var response = await client.GetAsync(ApiUri(ExportPath(format, type, deleted)));
            await EnsureSuccess(response);
            var mediaType = format == "json" ? "application/json" : "text/markdown";
            if (response.Content.Headers.ContentType?.MediaType != mediaType)
                throw new UiApiException("Serwer nie zwrócił pliku eksportu. Sprawdź wersję API.");
            var extension = format == "json" ? "json" : "md";
            // Use our own filename: server headers must never control local filesystem paths.
            return new(await response.Content.ReadAsByteArrayAsync(),
                $"lifeos-{(deleted ? "kosz" : "notatki")}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.{extension}", mediaType);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        { throw new UiApiException("Nie udało się pobrać eksportu. Sprawdź połączenie i spróbuj ponownie."); }
    }

    public static string ExportPath(string format, CaptureType? type, bool deleted) =>
        $"api/captures/export?format={Uri.EscapeDataString(format)}" + (type is null ? "" : $"&type={type}")
        + (deleted ? "&deleted=true" : "");

    private async Task<T> SendAsync<T>(HttpMethod method, string path, JsonTypeInfo<T>? typeInfo = null,
        HttpContent? body = null, bool noContent = false)
    {
        try
        {
            using var client = factory.CreateClient("LifeOsUi");
            using var request = new HttpRequestMessage(method, ApiUri(path));
            request.Content = body;
            using var response = await client.SendAsync(request);
            await EnsureSuccess(response);
            if (noContent)
            {
                if (response.StatusCode != System.Net.HttpStatusCode.NoContent) throw new JsonException();
                return default!;
            }
            return await response.Content.ReadFromJsonAsync(typeInfo!) ?? throw new JsonException();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new UiApiException("Brak odpowiedzi serwera. Jeśli zapisujesz notatkę, sprawdź jej zapis przed ponownym wysłaniem. Wpisany tekst pozostaje w formularzu.");
        }
        catch (JsonException) { throw new UiApiException("Nie udało się odczytać odpowiedzi serwera."); }
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var message = "Nie udało się wykonać operacji. Spróbuj ponownie później.";
        try
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (problem.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (problem.RootElement.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
                    && title.GetString() is { Length: > 0 } value) message = value;
                if (problem.RootElement.TryGetProperty("rawCaptureSaved", out var saved) && saved.ValueKind == JsonValueKind.True)
                    message = "Oryginał zapisano, ale nie udało się zapisać wyniku AI. Nie wysyłaj tej notatki ponownie.";
            }
        }
        catch (JsonException) { }
        throw new UiApiException(message);
    }
}

public sealed class UiApiException(string message) : Exception(message);
