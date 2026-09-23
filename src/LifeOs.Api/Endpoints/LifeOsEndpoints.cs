using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOs.Api.Data;
using LifeOs.Api.Llm;
using LifeOs.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOs.Api.Endpoints;

public static class LifeOsEndpoints
{
    public static void MapLifeOs(this WebApplication app)
    {
        app.MapPost("/api/captures", CreateCapture).Produces<Capture>(201).ProducesValidationProblem()
            .ProducesProblem(503).WithSummary("Zapisz notatkę i spróbuj klasyfikacji AI");
        app.MapGet("/api/captures", GetCaptures).Produces<Capture[]>().ProducesValidationProblem()
            .WithSummary("Historia notatek COMPLETED, od najnowszych według capturedAt");
        app.MapPost("/api/weekly-review", WeeklyReview).Produces<Models.WeeklyReview>()
            .ProducesProblem(502).ProducesProblem(503).WithSummary("Przegląd notatek COMPLETED z ostatnich 7 dni po polsku");
    }
    private static async Task<IResult> CreateCapture(CreateCaptureRequest request, LifeOsDbContext db,
        ILifeOsLlmService llm, TimeProvider clock, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = ["Tekst nie może być pusty."] });
        if (request.Source is null || !Enum.IsDefined(request.Source.Value))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["source"] = ["Podaj VOICE, TEXT lub OTHER."] });
        var log = loggerFactory.CreateLogger("Captures");
        var now = clock.GetUtcNow();
        var capture = new Capture
        {
            RawText = request.Text, Source = request.Source.Value, CreatedAt = now,
            CapturedAt = request.CapturedAt?.ToUniversalTime() ?? now, ProcessingState = ProcessingState.PENDING
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
    private static async Task<IResult> GetCaptures(LifeOsDbContext db, TimeProvider clock, CancellationToken cancellationToken,
        int? days = null, string? type = null, string? source = null, int limit = 100)
    {
        if (days is <= 0 or > 36500 || limit is < 1 or > 500) return InvalidQuery("days: 1–36500; limit: 1–500.");
        var query = db.Captures.AsNoTracking().Where(c => c.ProcessingState == ProcessingState.COMPLETED);
        if (days.HasValue)
        {
            var since = clock.GetUtcNow().AddDays(-days.Value);
            query = query.Where(c => c.CapturedAt >= since);
        }
        if (type is not null)
        {
            if (!Enum.GetNames<CaptureType>().Contains(type)) return InvalidQuery("Nieprawidłowy type.");
            var value = Enum.Parse<CaptureType>(type);
            query = query.Where(c => c.Type == value);
        }
        if (source is not null)
        {
            if (!Enum.GetNames<CaptureSource>().Contains(source)) return InvalidQuery("Nieprawidłowy source.");
            var value = Enum.Parse<CaptureSource>(source);
            query = query.Where(c => c.Source == value);
        }
        return Results.Ok(await query.OrderByDescending(c => c.CapturedAt).ThenByDescending(c => c.CreatedAt)
            .ThenBy(c => c.Id).Take(limit).ToListAsync(cancellationToken));
    }
    private static IResult InvalidQuery(string message) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["query"] = [message] });
    private static async Task<IResult> WeeklyReview(LifeOsDbContext db, ILifeOsLlmService llm,
        TimeProvider clock, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var end = clock.GetUtcNow();
        var start = end.AddDays(-7);
        var captures = await db.Captures.AsNoTracking()
            .Where(c => c.ProcessingState == ProcessingState.COMPLETED && c.CapturedAt >= start && c.CapturedAt <= end)
            .OrderBy(c => c.CapturedAt).ToListAsync(cancellationToken);
        try
        {
            var content = captures.Count == 0 ? ReviewContent.Empty : await llm.GenerateWeeklyReviewAsync(captures, cancellationToken);
            loggerFactory.CreateLogger("WeeklyReview").LogInformation("Weekly review generated from {Count} captures", captures.Count);
            return Results.Ok(Models.WeeklyReview.From(start, end, content));
        }
        catch (LlmException e) { return Results.Problem(statusCode: 502, title: e.Message); }
    }
}
