using LifeOs.Api.Audio;
using LifeOs.Api.Models;
using LifeOs.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace LifeOs.Api.Endpoints;

public static class AudioEndpoints
{
    public const long MaxFileBytes = 25_000_000;
    private const long MaxRequestBytes = MaxFileBytes + 64 * 1024;
    private static readonly Dictionary<string, string[]> Formats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".m4a"] = ["audio/mp4", "audio/m4a", "audio/x-m4a"],
        [".mp4"] = ["audio/mp4", "video/mp4"],
        [".mp3"] = ["audio/mpeg", "audio/mp3"],
        [".wav"] = ["audio/wav", "audio/wave", "audio/x-wav", "audio/vnd.wave"],
        [".webm"] = ["audio/webm", "video/webm"],
        [".ogg"] = ["audio/ogg", "application/ogg"],
        [".flac"] = ["audio/flac", "audio/x-flac"]
    };

    public static void MapAudioCaptures(this WebApplication app) =>
        app.MapPost("/api/captures/audio", Create)
            .Accepts<AudioUploadRequest>("multipart/form-data")
            .Produces<Capture>(201).ProducesValidationProblem().ProducesProblem(413)
            .ProducesProblem(415).ProducesProblem(502).ProducesProblem(503)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .WithSummary("Transkrybuj nagranie i zapisz notatkę VOICE");

    private static async Task<IResult> Create(HttpRequest request, IAudioTranscriptionService transcription,
        CaptureCreationService captures, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxRequestBytes) return TooLarge();
        var bodyLimit = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = MaxRequestBytes;
        if (!request.HasFormContentType || request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) != true)
            return Results.Problem(statusCode: 415, title: "Wyślij multipart/form-data z plikiem w polu file.");
        IFormCollection form;
        try
        {
            // ASP.NET disposes temporary buffering files at the end of the request.
            form = await request.ReadFormAsync(new FormOptions
            {
                MultipartBodyLengthLimit = MaxRequestBytes,
                ValueLengthLimit = 1024,
                ValueCountLimit = 1,
                MultipartHeadersLengthLimit = 4096
            }, cancellationToken);
        }
        catch (InvalidDataException) { return Invalid("Nieprawidłowy formularz lub plik większy niż 25 MB."); }
        catch (BadHttpRequestException e) when (e.StatusCode == 413) { return TooLarge(); }
        if (form.Files.Count != 1 || form.Files[0].Name != "file")
            return Invalid("Prześlij dokładnie jeden plik w polu file.");
        var file = form.Files[0];
        if (file.Length == 0) return Invalid("Nagranie jest puste.");
        if (file.Length > MaxFileBytes) return TooLarge();
        var extension = Path.GetExtension(file.FileName);
        var contentType = file.ContentType.Split(';')[0].Trim().ToLowerInvariant();
        if (!Formats.TryGetValue(extension, out var mediaTypes) ||
            (contentType is not ("" or "application/octet-stream") && !mediaTypes.Contains(contentType)))
            return Results.Problem(statusCode: 415, title: "Nieobsługiwany format lub typ pliku audio.");
        var log = loggerFactory.CreateLogger("AudioCaptures");
        log.LogInformation("Audio upload received, bytes {Bytes}, content type {ContentType}", file.Length, contentType);
        try
        {
            log.LogInformation("Transcription started");
            await using var stream = file.OpenReadStream();
            // Avoid forwarding a personal filename to the provider.
            var text = await transcription.TranscribeAsync(stream, "capture" + extension.ToLowerInvariant(),
                mediaTypes[0], cancellationToken);
            if (string.IsNullOrWhiteSpace(text)) throw new TranscriptionException("Nie rozpoznano mowy w nagraniu.");
            log.LogInformation("Transcription completed");
            return await captures.CreateFromTextAsync(text, CaptureSource.VOICE, null, cancellationToken);
        }
        catch (TranscriptionException e)
        {
            log.LogWarning("Audio transcription failed, error type {ErrorType}", e.GetType().Name);
            return Results.Problem(statusCode: 502, title: e.Message);
        }
    }

    private static IResult Invalid(string message) => Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [message] });
    private static IResult TooLarge() => Results.Problem(statusCode: 413, title: "Nagranie przekracza limit 25 MB.");

    public sealed class AudioUploadRequest
    {
        public required IFormFile File { get; init; }
    }
}
