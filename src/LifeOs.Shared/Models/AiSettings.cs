namespace LifeOs.Api.Models;

public sealed class NoteCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Instructions { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public CaptureType LegacyType { get; set; } = CaptureType.OTHER;
}
public sealed class AiSettings
{
    public List<NoteCategory> Categories { get; set; } = [];
    public string CaptureInstructions { get; set; } = "";
    public string ReviewInstructions { get; set; } = "";
}
public sealed record SettingsDocument(Guid Revision, int ActiveVersion, AiSettings Draft, AiSettings Active);
public sealed record SaveSettingsRequest(Guid Revision, AiSettings Draft);
public sealed record ActivateSettingsRequest(Guid Revision);
public sealed record PreviewSettingsRequest(AiSettings Settings, string Text, bool Review = false);
public sealed record SettingsPreview(Capture? Note, WeeklyReview? Review);
