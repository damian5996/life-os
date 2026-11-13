using System.Net.Http.Headers;
using System.Text.Json;
using LifeOs.Api.Llm;
using Microsoft.Extensions.Options;

namespace LifeOs.Api.Audio;

public sealed class OpenAiAudioTranscriptionService(HttpClient http, IOptions<LlmOptions> options,
    ILogger<OpenAiAudioTranscriptionService>? logger = null)
    : IAudioTranscriptionService
{
    public async Task<string> TranscribeAsync(Stream audioStream, string fileName, string? contentType,
        CancellationToken cancellationToken)
    {
        var config = options.Value;
        if (string.IsNullOrWhiteSpace(config.TranscriptionModel))
            throw new TranscriptionException("Nie skonfigurowano modelu transkrypcji.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(config.TranscriptionTimeoutSeconds));
        // FormFile streams are seekable. Buffer only other callers; keep the original bytes
        // available for one bounded retry and never use the suspect transcript as its prompt.
        using var buffer = audioStream.CanSeek ? null : new MemoryStream();
        try
        {
            if (buffer is not null) { await audioStream.CopyToAsync(buffer, deadline.Token); buffer.Position = 0; }
            var source = (Stream?)buffer ?? audioStream;
            var start = source.Position;
            var text = await TranscribeOnceAsync(source, fileName, contentType, config.TranscriptionModel, deadline.Token);
            if (!UnexpectedHangul(text, config.TranscriptionLanguage)) return text;
            logger?.LogWarning("Unexpected Hangul in Polish transcription; retrying original audio once");
            if (string.IsNullOrWhiteSpace(config.TranscriptionFallbackModel)) throw UnreliableSpeech();
            source.Position = start;
            text = await TranscribeOnceAsync(source, fileName, contentType, config.TranscriptionFallbackModel, deadline.Token);
            if (UnexpectedHangul(text, config.TranscriptionLanguage))
            {
                logger?.LogWarning("Polish transcription quality check failed after retry; no capture created");
                throw UnreliableSpeech();
            }
            return text;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TranscriptionException("Przekroczono czas transkrypcji. Nagranie pozostało na telefonie."); }
    }

    private static bool UnexpectedHangul(string text, string language) =>
        string.Equals(language, "pl", StringComparison.OrdinalIgnoreCase) && text.Any(c =>
            c is >= '\u1100' and <= '\u11FF' or >= '\u3130' and <= '\u318F' or
                >= '\uA960' and <= '\uA97F' or >= '\uAC00' and <= '\uD7AF' or >= '\uD7B0' and <= '\uD7FF' or
                >= '\uFFA0' and <= '\uFFDC');

    private static TranscriptionException UnreliableSpeech() => new(
        "Nie udało się wiarygodnie rozpoznać polskiej mowy. Notatka nie została utworzona. Nagranie pozostało na telefonie; ponów wysyłanie.");

    private async Task<string> TranscribeOnceAsync(Stream audioStream, string fileName, string? contentType,
        string model, CancellationToken cancellationToken)
    {
        var config = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(config.BaseUrl.TrimEnd('/') + "/"), "audio/transcriptions"));
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        using var form = new MultipartFormDataContent();
        var audio = new RetainedStreamContent(audioStream);
        audio.Headers.ContentType = new MediaTypeHeaderValue(contentType ?? "application/octet-stream");
        form.Add(audio, "file", fileName);
        form.Add(new StringContent(model), "model");
        form.Add(new StringContent(config.TranscriptionLanguage), "language");
        form.Add(new StringContent("json"), "response_format");
        form.Add(new StringContent("auto"), "chunking_strategy");
        // On silent recordings the provider can echo the prompt as a transcription.
        // Supply the language, but no suggested transcript or instruction text.
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
                throw new TranscriptionException("Nie wykryto mowy. Notatka nie została utworzona. Nagraj ponownie.", noSpeech: true);
            return text;
        }
        catch (HttpRequestException) { throw new TranscriptionException("Usługa transkrypcji jest niedostępna."); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TranscriptionException("Przekroczono czas transkrypcji."); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new TranscriptionException("Usługa transkrypcji zwróciła nieprawidłową odpowiedź."); }
    }

    // Disposing a request must not close the caller's audio stream before the retry.
    private sealed class RetainedStreamContent(Stream source) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = source.Length - source.Position; return true; }
        protected override Task SerializeToStreamAsync(Stream target, System.Net.TransportContext? context) => source.CopyToAsync(target);
        protected override Task SerializeToStreamAsync(Stream target, System.Net.TransportContext? context, CancellationToken ct) => source.CopyToAsync(target, ct);
    }
}
