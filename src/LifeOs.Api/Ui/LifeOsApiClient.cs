using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOs.Api.Models;
using Microsoft.AspNetCore.Components;

namespace LifeOs.Api.Ui;

public sealed class LifeOsApiClient(IHttpClientFactory factory, NavigationManager navigation)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public Task<Capture[]> GetCapturesAsync(CaptureType? type) => SendAsync<Capture[]>(HttpMethod.Get,
        "api/captures?limit=100" + (type is null ? "" : $"&type={type}"));

    public Task<Capture> CreateCaptureAsync(string text) => SendAsync<Capture>(HttpMethod.Post,
        "api/captures", new { text, source = "TEXT" });

    public Task<WeeklyReview> GenerateReviewAsync() => SendAsync<WeeklyReview>(HttpMethod.Post, "api/weekly-review");

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null)
    {
        try
        {
            using var client = factory.CreateClient("LifeOsUi");
            using var request = new HttpRequestMessage(method, navigation.ToAbsoluteUri(path));
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var message = "Nie udało się wykonać operacji. Spróbuj ponownie później.";
                try
                {
                    using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    if (problem.RootElement.TryGetProperty("title", out var title) && title.GetString() is { Length: > 0 } value)
                        message = value;
                    if (problem.RootElement.TryGetProperty("rawCaptureSaved", out var saved) && saved.ValueKind == JsonValueKind.True)
                        message = "Oryginał zapisano, ale nie udało się zapisać wyniku AI. Nie wysyłaj tej notatki ponownie.";
                }
                catch (JsonException) { }
                throw new UiApiException(message);
            }
            return await response.Content.ReadFromJsonAsync<T>(Json) ?? throw new JsonException();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new UiApiException("Brak odpowiedzi serwera. Jeśli zapisujesz notatkę, sprawdź jej zapis przed ponownym wysłaniem. Wpisany tekst pozostaje w formularzu.");
        }
        catch (JsonException) { throw new UiApiException("Nie udało się odczytać odpowiedzi serwera."); }
    }
}

public sealed class UiApiException(string message) : Exception(message);
