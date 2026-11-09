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
    public async Task ConcurrentRecordingInsertUsesPrimaryKeyAndPreservesOriginalDate()
    {
        using var app = new TestApp(postgres: Environment.GetEnvironmentVariable("LIFEOS_TEST_POSTGRES"));
        using var firstScope = app.Services.CreateScope();
        using var secondScope = app.Services.CreateScope();
        var db = firstScope.ServiceProvider.GetRequiredService<LifeOsDbContext>();
        await db.Database.MigrateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Llm.BeforeClassify = async _ => { entered.TrySetResult(); await release.Task; };
        var id = Guid.NewGuid();
        var date = DateTimeOffset.Parse("2026-03-29T01:59:59.1234567+01:00");
        var first = firstScope.ServiceProvider.GetRequiredService<LifeOs.Api.Services.CaptureCreationService>()
            .CreateFromTextAsync("Test offline", CaptureSource.VOICE, date, default, id);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var second = await secondScope.ServiceProvider.GetRequiredService<LifeOs.Api.Services.CaptureCreationService>()
                .CreateFromTextAsync("Test offline", CaptureSource.VOICE, date, default, id);
            Assert.Equal(201, Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(second).StatusCode);
        }
        finally { release.TrySetResult(); }
        await first;
        var capture = Assert.Single(await app.Captures(), x => x.Id == id);
        Assert.True(Math.Abs((capture.CapturedAt - date).TotalMicroseconds) < 1);
        Assert.Equal(ProcessingState.COMPLETED, capture.ProcessingState);
        Assert.Equal(1, app.Llm.ClassificationCalls);
    }

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
        using var audio = AudioTests.Upload();
        response = await client.PostAsync("/api/captures/audio", audio);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        capture = await response.Content.ReadFromJsonAsync<Capture>(LlmJson.Options);
        saved = (await app.Captures()).Single(c => c.Id == capture!.Id);
        Assert.Equal(CaptureSource.VOICE, saved.Source);
        Assert.Equal(app.Transcription.Text, saved.RawText);
        Assert.Equal(ProcessingState.COMPLETED, saved.ProcessingState);
        Assert.Equal("medytacja", Assert.Single(saved.Tags));
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
