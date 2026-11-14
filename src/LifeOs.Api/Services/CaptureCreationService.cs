using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOs.Api.Data;
using LifeOs.Api.Llm;
using LifeOs.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOs.Api.Services;

public sealed class CaptureCreationService(LifeOsDbContext db, ILifeOsLlmService llm,
    TimeProvider clock, ILoggerFactory loggerFactory)
{
    public async Task<IResult> CreateFromTextAsync(string text, CaptureSource source,
        DateTimeOffset? capturedAt, CancellationToken cancellationToken, Guid? recordingId = null)
    {
        var log = loggerFactory.CreateLogger("Captures");
        var now = clock.GetUtcNow();
        var capture = new Capture
        {
            Id = recordingId ?? Guid.NewGuid(),
            RawText = text, Source = source, CreatedAt = now,
            CapturedAt = capturedAt?.ToUniversalTime() ?? now, ProcessingState = ProcessingState.PENDING
        };
        log.LogInformation("Capture received {CaptureId}, source {Source}", capture.Id, capture.Source);
        db.Captures.Add(capture);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException e) when (recordingId.HasValue && e.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            db.Entry(capture).State = EntityState.Detached;
            return (await FindRecordingAsync(recordingId.Value, capturedAt!.Value, cancellationToken))!;
        }
        log.LogInformation("Capture persisted {CaptureId}", capture.Id);
        try
        {
            log.LogInformation("Classification started {CaptureId}", capture.Id);
            var result = await llm.ClassifyCaptureAsync(capture.RawText, cancellationToken);
            capture.Type = result.Type;
            capture.CategoryId = result.CategoryId;
            capture.CategoryName = result.CategoryName;
            capture.AiSettingsVersion = result.SettingsVersion;
            capture.Title = result.Title;
            capture.Summary = result.Summary;
            capture.Tags = result.Tags;
            capture.ActionRequired = result.ActionRequired;
            capture.MetadataJson = JsonSerializer.Serialize(result.Metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web)
                { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            capture.ProcessingState = ProcessingState.COMPLETED;
            log.LogInformation("Classification succeeded {CaptureId}", capture.Id);
        }
        catch (Exception e)
        {
            capture.ProcessingState = ProcessingState.FAILED;
            capture.ProcessingError = e is LlmException ? e.Message : "Nie udało się sklasyfikować notatki. Oryginał jest zapisany.";
            log.LogWarning("Classification failed {CaptureId}, error type {ErrorType}", capture.Id, e.GetType().Name);
        }
        // The raw insert is already committed. A disconnected client must not cancel this final update.
        using var saveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await db.SaveChangesAsync(saveTimeout.Token); }
        catch (Exception e) when (e is DbUpdateException or DbException or OperationCanceledException)
        {
            log.LogError("Enrichment persistence failed {CaptureId}, error type {ErrorType}", capture.Id, e.GetType().Name);
            return Results.Problem(statusCode: 503, title: "Oryginał zapisano, ale zapis wyniku AI nie powiódł się.",
                extensions: new Dictionary<string, object?> { ["captureId"] = capture.Id, ["rawCaptureSaved"] = true });
        }
        return Results.Created("/api/captures", capture);
    }

    public async Task<IResult?> FindRecordingAsync(Guid id, DateTimeOffset capturedAt, CancellationToken ct)
    {
        var existing = await db.Captures.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        if (existing is null) return null;
        if (existing.Source != CaptureSource.VOICE || Math.Abs((existing.CapturedAt - capturedAt).TotalMilliseconds) >= 1)
            return Results.Conflict(new { message = "Identyfikator należy do innego nagrania." });
        // Even PENDING has its original transcription committed. Never create another copy,
        // and never restore a deliberately deleted note on an upload retry.
        return Results.Created("/api/captures", existing);
    }
}
