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
        app.MapGet("/api/captures/export", ExportCaptures)
            .Produces(200, contentType: "text/markdown", additionalContentTypes: ["application/json"])
            .ProducesValidationProblem().ProducesProblem(503)
            .WithSummary("Eksport wszystkich notatek wybranego typu i zakresu (poza koszem lub kosz), także FAILED/PENDING");
        app.MapDelete("/api/captures/{id:guid}", DeleteCapture).Produces(204).ProducesProblem(503)
            .WithSummary("Przenieś notatkę do kosza (odwracalnie)");
        app.MapPost("/api/captures/{id:guid}/restore", RestoreCapture).Produces(204).Produces(404).ProducesProblem(503)
            .WithSummary("Przywróć notatkę z kosza");
        app.MapPost("/api/review", GenerateReview).Produces<Models.WeeklyReview>().ProducesValidationProblem()
            .ProducesProblem(502).ProducesProblem(503).WithSummary("Przegląd notatek: week, month lub six-months");
        app.MapPost("/api/weekly-review", GenerateReview).Produces<Models.WeeklyReview>().ProducesValidationProblem()
            .ProducesProblem(502).ProducesProblem(503).WithSummary("Przegląd notatek (domyślnie ostatni tydzień)");
    }
    private static async Task<IResult> CreateCapture(CreateCaptureRequest request,
        LifeOs.Api.Services.CaptureCreationService captures, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = ["Tekst nie może być pusty."] });
        if (request.Source is null || !Enum.IsDefined(request.Source.Value))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["source"] = ["Podaj VOICE, TEXT lub OTHER."] });
        return await captures.CreateFromTextAsync(request.Text, request.Source.Value, request.CapturedAt, cancellationToken);
    }
    private static async Task<IResult> GetCaptures(LifeOsDbContext db, TimeProvider clock, CancellationToken cancellationToken,
        int? days = null, string? type = null, string? source = null, int limit = 100, bool deleted = false)
    {
        if (days is <= 0 or > 36500 || limit is < 1 or > 500) return InvalidQuery("days: 1–36500; limit: 1–500.");
        var query = db.Captures.AsNoTracking().Where(c => deleted
            ? c.DeletedAt != null : c.DeletedAt == null && c.ProcessingState == ProcessingState.COMPLETED);
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
    private static async Task<IResult> ExportCaptures(LifeOsDbContext db, TimeProvider clock, HttpContext context,
        CancellationToken cancellationToken, string format = "markdown", string? type = null, bool deleted = false)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (format is not ("markdown" or "json")) return InvalidQuery("format: markdown lub json.");
        var query = db.Captures.AsNoTracking().Where(c => deleted ? c.DeletedAt != null : c.DeletedAt == null);
        if (type is not null)
        {
            if (!Enum.GetNames<CaptureType>().Contains(type)) return InvalidQuery("Nieprawidłowy type.");
            var value = Enum.Parse<CaptureType>(type);
            query = query.Where(c => c.Type == value);
        }
        var captures = await query.OrderByDescending(c => c.CapturedAt).ThenByDescending(c => c.CreatedAt)
            .ThenBy(c => c.Id).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var bytes = format == "json" ? Services.CaptureExport.ToJson(captures, now, type, deleted)
            : Services.CaptureExport.ToMarkdown(captures, now, type, deleted);
        var extension = format == "json" ? "json" : "md";
        var filename = $"lifeos-{(deleted ? "kosz" : "notatki")}-{type ?? "wszystkie"}-{now:yyyyMMdd-HHmmss}Z.{extension}";
        return Results.File(bytes, format == "json" ? "application/json; charset=utf-8" : "text/markdown; charset=utf-8", filename);
    }
    private static async Task<IResult> DeleteCapture(Guid id, LifeOsDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var capture = await db.Captures.FindAsync([id], cancellationToken);
        if (capture is not null && capture.DeletedAt is null)
        {
            capture.DeletedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }
        return Results.NoContent();
    }
    private static async Task<IResult> RestoreCapture(Guid id, LifeOsDbContext db, CancellationToken cancellationToken)
    {
        var capture = await db.Captures.FindAsync([id], cancellationToken);
        if (capture is null) return Results.NotFound();
        capture.DeletedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }
    private static async Task<IResult> GenerateReview(LifeOsDbContext db, ILifeOsLlmService llm,
        TimeProvider clock, ILoggerFactory loggerFactory, CancellationToken cancellationToken, string period = "week",
        string? from = null, string? to = null)
    {
        var end = clock.GetUtcNow();
        if (period is not ("week" or "month" or "six-months" or "custom"))
            return InvalidQuery("period: week, month, six-months lub custom.");
        var start = period switch
        {
            "month" => end.AddMonths(-1),
            "six-months" => end.AddMonths(-6),
            _ => end.AddDays(-7)
        };
        var queryEnd = end;
        var exclusiveEnd = DateTimeOffset.MaxValue;
        if (period == "custom")
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(end, zone).DateTime);
            if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var first) ||
                !DateOnly.TryParseExact(to, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var last) || first > last || last > today)
                return InvalidQuery("Wybierz poprawne daty od–do, nie późniejsze niż dzisiaj.");
            start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(first.ToDateTime(TimeOnly.MinValue), zone));
            exclusiveEnd = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(last.AddDays(1).ToDateTime(TimeOnly.MinValue), zone));
            if (exclusiveEnd <= end) end = exclusiveEnd.AddTicks(-1);
        }
        else if (from is not null || to is not null) return InvalidQuery("Daty od–do wymagają period=custom.");
        var captures = await db.Captures.AsNoTracking()
            .Where(c => c.DeletedAt == null && c.ProcessingState == ProcessingState.COMPLETED && c.CapturedAt >= start && c.CapturedAt <= queryEnd && c.CapturedAt < exclusiveEnd)
            .OrderBy(c => c.CapturedAt).ToListAsync(cancellationToken);
        try
        {
            var content = captures.Count == 0 ? ReviewContent.Empty : await llm.GenerateReviewAsync(captures, start, end, cancellationToken);
            loggerFactory.CreateLogger("WeeklyReview").LogInformation("Weekly review generated from {Count} captures", captures.Count);
            return Results.Ok(Models.WeeklyReview.From(start, end, content));
        }
        catch (LlmException e) { return Results.Problem(statusCode: 502, title: e.Message); }
    }
}
