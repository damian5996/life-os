using System.Net.Http.Headers;
using System.Text.Json;
using LifeOs.Api.Llm;
using Microsoft.Extensions.Options;

namespace LifeOs.Api.Audio;

public sealed class OpenAiAudioTranscriptionService(HttpClient http, IOptions<LlmOptions> options)
    : IAudioTranscriptionService
{
    public async Task<string> TranscribeAsync(Stream audioStream, string fileName, string? contentType,
        CancellationToken cancellationToken)
    {
        var config = options.Value;
        if (string.IsNullOrWhiteSpace(config.TranscriptionModel))
            throw new TranscriptionException("Nie skonfigurowano modelu transkrypcji.");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(config.BaseUrl.TrimEnd('/') + "/"), "audio/transcriptions"));
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        using var form = new MultipartFormDataContent();
        var audio = new StreamContent(audioStream);
        audio.Headers.ContentType = new MediaTypeHeaderValue(contentType ?? "application/octet-stream");
        form.Add(audio, "file", fileName);
        form.Add(new StringContent(config.TranscriptionModel), "model");
        form.Add(new StringContent(config.TranscriptionLanguage), "language");
        form.Add(new StringContent("json"), "response_format");
        form.Add(new StringContent("Naturalna wypowiedź po polsku, także potoczna i z urwanymi zdaniami. " +
            "Zachowaj angielskie terminy: .NET, Azure, backend, content, coaching, API. " +
            "Zapisuj tylko usłyszaną mowę, bez dopisywania treści do ciszy."), "prompt");
        request.Content = form;
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new TranscriptionException($"Usługa transkrypcji zwróciła HTTP {(int)response.StatusCode}.");
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            var text = body.RootElement.GetProperty("text").GetString();
            if (string.IsNullOrWhiteSpace(text))
                throw new TranscriptionException("Nie rozpoznano mowy w nagraniu.");
            return text;
        }
        catch (HttpRequestException) { throw new TranscriptionException("Usługa transkrypcji jest niedostępna."); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TranscriptionException("Przekroczono czas transkrypcji."); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new TranscriptionException("Usługa transkrypcji zwróciła nieprawidłową odpowiedź."); }
    }
}
