using System.Net;
using System.Text;
using System.Text.Json;
using LifeOs.Api.Data;
using LifeOs.Api.Llm;
using LifeOs.Api.Models;
using LifeOs.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Api.Tests;

public sealed class ExportTests
{
    [Fact]
    public async Task JsonExportsAllActiveNotesWithoutHistoryLimitOrLlmCalls()
    {
        using var app = new TestApp();
        await app.Seed(Enumerable.Range(0, 501).Select(i => -i).ToArray());
        await app.SeedState(ProcessingState.FAILED, -1);
        await app.SeedState(ProcessingState.PENDING, 0);
        var response = await app.CreateClient().GetAsync("/api/captures/export?format=json");
        response.EnsureSuccessStatusCode();
        AssertDownload(response, "application/json", ".json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(TestApp.Now, json.RootElement.GetProperty("exportedAt").GetDateTimeOffset());
        Assert.Equal(503, json.RootElement.GetProperty("count").GetInt32());
        var captures = json.RootElement.GetProperty("captures").Deserialize<Capture[]>(LlmJson.Options)!;
        Assert.Equal(503, captures.Length);
        Assert.Contains(captures, c => c.ProcessingState == ProcessingState.FAILED);
        Assert.Contains(captures, c => c.ProcessingState == ProcessingState.PENDING);
        Assert.Equal(captures.OrderByDescending(c => c.CapturedAt).Select(c => c.Id), captures.Select(c => c.Id));
        Assert.Equal(0, app.Llm.ClassificationCalls);
        Assert.Equal(0, app.Llm.ReviewCalls);
    }

    [Theory]
    [InlineData("markdown")]
    [InlineData("json")]
    public async Task ExportsRespectTypeFilterAndKeepTrashSeparate(string format)
    {
        using var app = new TestApp();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LifeOsDbContext>();
            db.AddRange(
                new Capture { RawText = "ACTIVE_TASK", Type = CaptureType.TASK, CapturedAt = TestApp.Now },
                new Capture { RawText = "ACTIVE_IDEA", Type = CaptureType.IDEA, CapturedAt = TestApp.Now },
                new Capture { RawText = "TRASH_TASK", Type = CaptureType.TASK, CapturedAt = TestApp.Now, DeletedAt = TestApp.Now });
            await db.SaveChangesAsync();
        }
        var client = app.CreateClient();
        var active = await client.GetStringAsync($"/api/captures/export?format={format}&type=TASK");
        Assert.Contains("ACTIVE_TASK", active);
        Assert.DoesNotContain("ACTIVE_IDEA", active);
        Assert.DoesNotContain("TRASH_TASK", active);
        var trash = await client.GetStringAsync($"/api/captures/export?format={format}&type=TASK&deleted=true");
        Assert.Contains("TRASH_TASK", trash);
        Assert.DoesNotContain("ACTIVE_TASK", trash);
        Assert.DoesNotContain("ACTIVE_IDEA", trash);
    }

    [Fact]
    public async Task DefaultExportIsUtf8MarkdownWithOriginalAndAiSummary()
    {
        using var app = new TestApp();
        const string raw = "  Zażółć gęślą jaźń\r\nDruga linia  ";
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LifeOsDbContext>();
            db.Add(new Capture
            {
                RawText = raw, Title = "Tenis", Summary = "Czujesz więcej energii", Tags = ["sport"],
                CapturedAt = TestApp.Now, CreatedAt = TestApp.Now, Type = CaptureType.FIELD_NOTE,
                Source = CaptureSource.VOICE, ProcessingState = ProcessingState.COMPLETED
            });
            await db.SaveChangesAsync();
        }
        var response = await app.CreateClient().GetAsync("/api/captures/export");
        response.EnsureSuccessStatusCode();
        AssertDownload(response, "text/markdown", ".md");
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains(raw, text);
        Assert.Contains("Czujesz więcej energii", text);
        Assert.Contains("sport", text);
        Assert.Contains("Głos", text);
        Assert.Contains("20:00 (Europe/Warsaw)", text);
        Assert.Contains("Liczba notatek: 1", text);
    }

    [Theory]
    [InlineData("format=csv")]
    [InlineData("format=")]
    [InlineData("type=UNKNOWN")]
    [InlineData("type=1")]
    public async Task InvalidExportOptionsReturn400(string query)
    {
        using var app = new TestApp();
        var response = await app.CreateClient().GetAsync("/api/captures/export?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EmptyExportIsStillAValidDownload()
    {
        using var app = new TestApp();
        var client = app.CreateClient();
        var response = await client.GetAsync("/api/captures/export");
        response.EnsureSuccessStatusCode();
        AssertDownload(response, "text/markdown", ".md");
        Assert.Contains("Brak notatek w wybranym zakresie.", await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/captures/export?format=json"));
        Assert.Empty(json.RootElement.GetProperty("captures").EnumerateArray());
    }

    [Fact]
    public void MarkdownKeepsEmbeddedCodeAndHtmlLiteralAndEscapesGeneratedFields()
    {
        var capture = new Capture
        {
            RawText = "  ```\n<script>alert('test')</script>\n````\n# Oryginał  ",
            Title = "[Tytuł](https://example.test)\n# Nowy nagłówek",
            Summary = "<img src=x> **tekst**", MetadataJson = "{\"test\":\"```\"}"
        };
        var markdown = Encoding.UTF8.GetString(CaptureExport.ToMarkdown([capture], TestApp.Now, null, false));
        Assert.Contains("`````text", markdown);
        Assert.Contains(capture.RawText, markdown);
        Assert.Contains("&lt;img src=x&gt; \\*\\*tekst\\*\\*", markdown);
        Assert.DoesNotContain("\n# Nowy nagłówek", markdown);
        Assert.Contains(capture.MetadataJson, markdown);
    }

    [Fact]
    public void JsonPreservesAllCaptureFieldsForPortability()
    {
        var capture = new Capture
        {
            RawText = "  Tekst\r\nżółć  ", CreatedAt = TestApp.Now, CapturedAt = TestApp.Now.AddDays(-3),
            DeletedAt = TestApp.Now, Source = CaptureSource.OTHER, Type = CaptureType.IDEA,
            Title = "Pomysł", Summary = "Rozważasz zmianę", Tags = ["praca", "rozwój"],
            ActionRequired = false, MetadataJson = "{\"location\":\"Łódź\"}",
            ProcessingState = ProcessingState.FAILED, ProcessingError = "Test błędu"
        };
        using var json = JsonDocument.Parse(CaptureExport.ToJson([capture], TestApp.Now, "IDEA", true));
        var restored = json.RootElement.GetProperty("captures")[0].Deserialize<Capture>(LlmJson.Options)!;
        Assert.Equal(JsonSerializer.Serialize(capture, LlmJson.Options), JsonSerializer.Serialize(restored, LlmJson.Options));
        Assert.True(json.RootElement.GetProperty("scope").GetProperty("deleted").GetBoolean());
    }

    private static void AssertDownload(HttpResponseMessage response, string mediaType, string extension)
    {
        Assert.Equal(mediaType, response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.EndsWith(extension, response.Content.Headers.ContentDisposition.FileNameStar);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }
}
