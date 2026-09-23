namespace LifeOs.Api.Models;

public enum CaptureSource { VOICE, TEXT, OTHER }
public enum CaptureType { FIELD_NOTE, TASK, CONTENT_IDEA, IDEA, OTHER }
public enum ProcessingState { PENDING, COMPLETED, FAILED }

public sealed class Capture
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public required string RawText { get; set; }
    public CaptureSource Source { get; set; }
    public CaptureType? Type { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string[] Tags { get; set; } = [];
    public bool? ActionRequired { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public ProcessingState ProcessingState { get; set; }
    public string? ProcessingError { get; set; }
}

public sealed record CreateCaptureRequest(string? Text, DateTimeOffset? CapturedAt, CaptureSource? Source);
