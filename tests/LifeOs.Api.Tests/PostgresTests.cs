using System.Net;
using System.Net.Http.Json;
using LifeOs.Api.Data;
using LifeOs.Api.Llm;
using LifeOs.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Api.Tests;

public sealed class PostgresTests
{
    [PostgresFact]
    public async Task MigrationAndCaptureFlowRoundTripThroughPostgres()
    {
        // Use an empty disposable database: this test deliberately applies real migrations.
        using var app = new TestApp(postgres: Environment.GetEnvironmentVariable("LIFEOS_TEST_POSTGRES"));
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<LifeOsDbContext>().Database.MigrateAsync();
        var client = app.CreateClient();
        const string raw = "  medytacja była spoko\n satysfakcja 7 na 10  ";
        var response = await client.PostAsJsonAsync("/api/captures", new
            { text = raw, source = "VOICE", capturedAt = "2026-09-22T18:20:00+02:00" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var capture = await response.Content.ReadFromJsonAsync<Capture>(LlmJson.Options);
        var saved = (await app.Captures()).Single(c => c.Id == capture!.Id);
        Assert.Equal(raw, saved.RawText);
        Assert.Equal(ProcessingState.COMPLETED, saved.ProcessingState);
        Assert.Equal("medytacja", Assert.Single(saved.Tags));
        using var metadata = System.Text.Json.JsonDocument.Parse(saved.MetadataJson);
        Assert.Equal(7, metadata.RootElement.GetProperty("satisfaction").GetDouble());
        Assert.Equal(DateTimeOffset.Parse("2026-09-22T16:20:00Z"), saved.CapturedAt);
        app.Llm.Fail = true;
        response = await client.PostAsJsonAsync("/api/captures", new { text = "Oryginał mimo awarii", source = "TEXT" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        capture = await response.Content.ReadFromJsonAsync<Capture>(LlmJson.Options);
        saved = (await app.Captures()).Single(c => c.Id == capture!.Id);
        Assert.Equal(ProcessingState.FAILED, saved.ProcessingState);
        Assert.Equal("Oryginał mimo awarii", saved.RawText);
        var history = await client.GetFromJsonAsync<Capture[]>("/api/captures?days=7&type=FIELD_NOTE&source=VOICE&limit=100", LlmJson.Options);
        Assert.Contains(history!, c => c.RawText == raw);
        app.Llm.Fail = false;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/weekly-review", null)).StatusCode);
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LIFEOS_TEST_POSTGRES")))
            Skip = "Set LIFEOS_TEST_POSTGRES to an empty disposable PostgreSQL database to run integration verification.";
    }
}
