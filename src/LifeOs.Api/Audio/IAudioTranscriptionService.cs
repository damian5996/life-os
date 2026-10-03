namespace LifeOs.Api.Audio;

public interface IAudioTranscriptionService
{
    Task<string> TranscribeAsync(Stream audioStream, string fileName, string? contentType,
        CancellationToken cancellationToken);
}

public sealed class TranscriptionException(string message) : Exception(message);
