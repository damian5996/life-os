using System.Net;
using System.Net.Http.Json;
using LifeOs.Api.Data;
using LifeOs.Api.Llm;
using LifeOs.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Api.Tests;

public sealed class DateRangeAndSyncTests
{
    [Theory]
    [InlineData("2026-03-29", "2026-03-28T23:00:00Z", "2026-03-29T22:00:00Z")]
    [InlineData("2025-10-26", "2025-10-25T22:00:00Z", "2025-10-26T23:00:00Z")]
    public async Task CustomRangeIncludesWholePolishDayAcrossDst(string day, string startText, string endText)
    {
        using var app = new TestApp();
        var start = DateTimeOffset.Parse(startText); var end = DateTimeOffset.Parse(endText);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LifeOsDbContext>();
            foreach (var date in new[] { start.AddTicks(-1), start, end.AddTicks(-1), end })
                db.Captures.Add(new Capture { RawText = "Test", Source = CaptureSource.TEXT,
                    CreatedAt = TestApp.Now, CapturedAt = date, ProcessingState = ProcessingState.COMPLETED });
            await db.SaveChangesAsync();
        }
        var response = await app.CreateClient().PostAsync($"/api/review?period=custom&from={day}&to={day}", null);
        response.EnsureSuccessStatusCode();
        Assert.Equal(new[] { start, end.AddTicks(-1) }, app.Llm.ReviewCaptures.Select(c => c.CapturedAt));
    }
    [Theory]
    [InlineData("period=custom")]
    [InlineData("period=custom&from=2026-09-20&to=2026-09-19")]
    [InlineData("period=custom&from=2026-09-20&to=2026-09-23")]
    [InlineData("period=custom&from=oops&to=2026-09-20")]
    [InlineData("period=week&from=2026-09-20")]
    public async Task InvalidRangeIsRejected(string query)
    {
        using var app = new TestApp();
        Assert.Equal(HttpStatusCode.BadRequest, (await app.CreateClient().PostAsync("/api/review?" + query, null)).StatusCode);
        Assert.Equal(0, app.Llm.ReviewCalls);
    }
    [Fact]
    public async Task RetryUsesSameCaptureAndRecordingTimeEvenAfterTrash()
    {
        using var app = new TestApp(); var client = app.CreateClient();
        var id = Guid.NewGuid(); var date = TestApp.Now.AddDays(-15);
        using var first = Upload(id, date);
        var response = await client.PostAsync("/api/captures/audio", first);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<Capture>(LlmJson.Options))!;
        Assert.Equal(id, saved.Id); Assert.Equal(date, saved.CapturedAt); Assert.Equal(TestApp.Now, saved.CreatedAt);
        await client.DeleteAsync($"/api/captures/{id}");
        using var retry = Upload(id, date);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/captures/audio", retry)).StatusCode);
        Assert.NotNull(Assert.Single(await app.Captures()).DeletedAt);
        Assert.Equal(1, app.Transcription.Calls); Assert.Equal(1, app.Llm.ClassificationCalls);
        using var conflict = Upload(id, date.AddDays(1));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/captures/audio", conflict)).StatusCode);
    }
    [Fact]
    public async Task MissingOrInvalidRecordingMetadataIsRejected()
    {
        using var app = new TestApp();
        using var form = Upload(Guid.NewGuid(), TestApp.Now);
        form.Add(new StringContent("invalid"), "capturedAt");
        Assert.Equal(HttpStatusCode.BadRequest, (await app.CreateClient().PostAsync("/api/captures/audio", form)).StatusCode);
        Assert.Equal(0, app.Transcription.Calls);
    }
    private static MultipartFormDataContent Upload(Guid id, DateTimeOffset date)
    {
        var form = new MultipartFormDataContent();
        var audio = new ByteArrayContent([1, 2, 3]);
        audio.Headers.ContentType = new("audio/mp4");
        form.Add(audio, "file", "capture.m4a");
        form.Add(new StringContent(id.ToString()), "recordingId");
        form.Add(new StringContent(date.ToString("O")), "capturedAt");
        return form;
    }
}
