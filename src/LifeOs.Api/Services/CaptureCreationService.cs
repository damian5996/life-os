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
        DateTimeOffset? capturedAt, CancellationToken cancellationToken)
    {
        var log = loggerFactory.CreateLogger("Captures");
        var now = clock.GetUtcNow();
        var capture = new Capture
        {
            RawText = text, Source = source, CreatedAt = now,
            CapturedAt = capturedAt?.ToUniversalTime() ?? now, ProcessingState = ProcessingState.PENDING
        };
        log.LogInformation("Capture received {CaptureId}, source {Source}", capture.Id, capture.Source);
        db.Captures.Add(capture);
        await db.SaveChangesAsync(cancellationToken);
        log.LogInformation("Capture persisted {CaptureId}", capture.Id);
        try
        {
            log.LogInformation("Classification started {CaptureId}", capture.Id);
            var result = await llm.ClassifyCaptureAsync(capture.RawText, cancellationToken);
            capture.Type = result.Type;
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
}
