using LifeOs.Api.Llm;
using LifeOs.Api.Models;
using LifeOs.Api.Services;

namespace LifeOs.Api.Endpoints;

public static class AdministrationEndpoints
{
    public static void MapAdministration(this WebApplication app)
    {
        var group = app.MapGroup("/api/administration");
        group.MapGet("/settings", (AiSettingsStore store, CancellationToken ct) => store.ReadAsync(ct));
        group.MapGet("/defaults", () => AiSettingsStore.Defaults());
        group.MapPut("/settings", async (SaveSettingsRequest request, AiSettingsStore store, CancellationToken ct) =>
        {
            var error = AiSettingsStore.Validate(request.Draft);
            if (error is not null) return Invalid(error);
            var current = await store.ReadAsync(ct);
            if (current.Active.Categories.Any(c => !request.Draft.Categories.Any(d => d.Id == c.Id)))
                return Invalid("Zachowaj istniejące kategorie. Niepotrzebne kategorie możesz wyłączyć, aby zachować historię.");
            var result = await store.SaveAsync(request.Revision, request.Draft, false, ct);
            return result is null ? Conflict() : Results.Ok(result);
        });
        group.MapPost("/activate", async (ActivateSettingsRequest request, AiSettingsStore store, CancellationToken ct) =>
        {
            var current = await store.ReadAsync(ct);
            if (current.Revision != request.Revision) return Conflict();
            var error = AiSettingsStore.Validate(current.Draft);
            if (error is not null) return Invalid(error);
            var result = await store.SaveAsync(request.Revision, current.Draft, true, ct);
            return result is null ? Conflict() : Results.Ok(result);
        });
        group.MapPost("/preview", async (PreviewSettingsRequest request, OpenAiCompatibleLlmService llm, TimeProvider clock, CancellationToken ct) =>
        {
            var error = AiSettingsStore.Validate(request.Settings);
            if (error is not null) return Invalid(error);
            if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 20000) return Invalid("Wpisz przykład o długości do 20000 znaków.");
            try
            {
                var now = clock.GetUtcNow();
                var capture = new Capture { RawText = request.Text, Source = CaptureSource.TEXT, CapturedAt = now, CreatedAt = now };
                if (request.Review)
                    return Results.Ok(new SettingsPreview(null, WeeklyReview.From(now, now,
                        await llm.ReviewWithPromptAsync(request.Settings.ReviewInstructions, [capture], now, now, ct))));
                var result = await llm.ClassifyWithSettingsAsync(request.Settings, 0, request.Text, ct);
                capture.Type = result.Type; capture.CategoryId = result.CategoryId; capture.CategoryName = result.CategoryName;
                capture.Title = result.Title; capture.Summary = result.Summary; capture.Tags = result.Tags;
                capture.ActionRequired = result.ActionRequired; capture.ProcessingState = ProcessingState.COMPLETED;
                return Results.Ok(new SettingsPreview(capture, null));
            }
            catch (LlmException e) { return Results.Problem(statusCode: 502, title: e.Message); }
        });
        app.MapGet("/api/categories", async (AiSettingsStore store, CancellationToken ct) =>
            (await store.ReadAsync(ct)).Active.Categories);
    }
    private static IResult Invalid(string message) => Results.Problem(statusCode: 400, title: message);
    private static IResult Conflict() => Results.Problem(statusCode: 409, title: "Ustawienia zmieniły się w innym oknie. Wczytaj je ponownie przed zapisem.");
}
