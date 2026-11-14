using System.Net.Http.Headers;
using System.Text;
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
        if (config.RequireApiKey && string.IsNullOrWhiteSpace(config.ApiKey))
            throw new TranscriptionException("Brak klucza AI dla LifeOS Dev. Nagranie pozostaje na telefonie do późniejszej synchronizacji.");
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
            if (!ContainsNonLatinLetters(text)) return text;
            logger?.LogWarning("Unexpected alphabet in Polish transcription; retrying original audio once");
            if (string.IsNullOrWhiteSpace(config.TranscriptionFallbackModel)) throw UnreliableSpeech();
            source.Position = start;
            text = await TranscribeOnceAsync(source, fileName, contentType, config.TranscriptionFallbackModel, deadline.Token);
            if (ContainsNonLatinLetters(text))
            {
                logger?.LogWarning("Polish transcription quality check failed after retry; no capture created");
                throw UnreliableSpeech();
            }
            return text;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TranscriptionException("Przekroczono czas transkrypcji. Nagranie pozostało na telefonie."); }
    }

    private static bool ContainsNonLatinLetters(string text)
    {
        // Polish and English technical terms use Latin letters. Check Unicode scalars
        // rather than UTF-16 chars so supplementary scripts cannot bypass the check.
        // Punctuation, numbers, emoji and combining accents are not letters.
        foreach (var rune in text.EnumerateRunes())
        {
            if (!Rune.IsLetter(rune)) continue;
            if (rune.Value is >= 0x0041 and <= 0x007A or >= 0x00C0 and <= 0x024F or
                >= 0x1E00 and <= 0x1EFF or
                >= 0x2C60 and <= 0x2C7F or >= 0xA720 and <= 0xA7FF or
                >= 0xAB30 and <= 0xAB6F or >= 0xFF21 and <= 0xFF5A) continue;
            return true;
        }
        return false;
    }

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
        // LifeOS records Polish speech. Do not permit environment overrides to
        // switch this to auto-detection or another language.
        form.Add(new StringContent("pl"), "language");
        form.Add(new StringContent("json"), "response_format");
        if (model != "whisper-1") form.Add(new StringContent("auto"), "chunking_strategy");
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
