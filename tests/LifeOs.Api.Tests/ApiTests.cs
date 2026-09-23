using System.Net;
using System.Net.Http.Json;
using System.Text;
using LifeOs.Api.Data;
using LifeOs.Api.Llm;
using LifeOs.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LifeOs.Api.Tests;

public sealed class ApiTests
{
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" \r\n ")]
    public async Task EmptyTextIsRejected(string? text)
    {
        using var app = new TestApp();
        var response = await app.CreateClient().PostAsJsonAsync("/api/captures", new { text, source = "TEXT" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await app.Captures());
        Assert.Equal(0, app.Llm.ClassificationCalls);
    }

    [Theory]
    [InlineData("TEXT")] [InlineData("VOICE")] [InlineData("OTHER")]
    public async Task SavesOriginalBeforeCallingLlm(string source)
    {
        using var app = new TestApp();
        const string raw = "  dzisiaj medytacja była spoko\r\n satysfakcja 7 na 10  ";
        app.Llm.BeforeClassify = async text =>
        {
            var saved = Assert.Single(await app.Captures());
            Assert.Equal(raw, saved.RawText);
            Assert.Equal(raw, text);
            Assert.Equal(ProcessingState.PENDING, saved.ProcessingState);
        };
        var response = await app.CreateClient().PostAsJsonAsync("/api/captures", new
            { text = raw, source, capturedAt = "2026-09-22T18:20:00+02:00" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = Assert.Single(await app.Captures());
        Assert.Equal(raw, saved.RawText);
        Assert.Equal(Enum.Parse<CaptureSource>(source), saved.Source);
        Assert.Equal(ProcessingState.COMPLETED, saved.ProcessingState);
        Assert.Equal(DateTimeOffset.Parse("2026-09-22T16:20:00Z"), saved.CapturedAt);
        Assert.Equal(TimeSpan.Zero, saved.CapturedAt.Offset);
        Assert.Contains("7", saved.MetadataJson);
    }

    [Theory]
    [InlineData("{\"text\":\"x\",\"source\":\"FAX\"}")]
    [InlineData("{\"text\":\"x\",\"source\":9}")]
    [InlineData("{\"text\":\"x\"}")]
    [InlineData("{\"text\":\"x\",\"source\":\"TEXT\",\"capturedAt\":\"bad\"}")]
    public async Task InvalidRequestsReturn400(string body)
    {
        using var app = new TestApp();
        var response = await app.CreateClient().PostAsync("/api/captures", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await app.Captures());
    }

    [Fact]
    public async Task LlmFailureStillReturnsSavedCapture()
    {
        using var app = new TestApp();
        app.Llm.Fail = true;
        var response = await app.CreateClient().PostAsJsonAsync("/api/captures", new { text = "Żółć i spokój", source = "VOICE" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var capture = Assert.Single(await app.Captures());
        Assert.Equal("Żółć i spokój", capture.RawText);
        Assert.Equal(ProcessingState.FAILED, capture.ProcessingState);
        Assert.NotNull(capture.ProcessingError);
        Assert.Null(capture.Type);
        Assert.Empty((await app.CreateClient().GetFromJsonAsync<Capture[]>("/api/captures", LlmJson.Options))!);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task DatabaseFailuresDoNotClaimEnrichmentWasSaved(int failOnSave)
    {
        using var app = new TestApp(failOnSave);
        var response = await app.CreateClient().PostAsJsonAsync("/api/captures", new { text = "Notatka", source = "TEXT" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var captures = await app.Captures();
        if (failOnSave == 1)
        {
            Assert.Empty(captures);
            Assert.Equal(0, app.Llm.ClassificationCalls);
        }
        else
        {
            Assert.Equal(ProcessingState.PENDING, Assert.Single(captures).ProcessingState);
            Assert.Contains("rawCaptureSaved", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task EmptyReviewDoesNotCallLlm()
    {
        using var app = new TestApp();
        var response = await app.CreateClient().PostAsync("/api/weekly-review", null);
        response.EnsureSuccessStatusCode();
        var review = await response.Content.ReadFromJsonAsync<WeeklyReview>();
        Assert.Empty(review!.KeyObservations);
        Assert.Null(review.SuggestedExperiment);
        Assert.Equal(TimeSpan.FromDays(7), review.PeriodEnd - review.PeriodStart);
        Assert.Equal(0, app.Llm.ReviewCalls);
    }

    [Fact]
    public async Task OnlyIncompleteNotesReturnEmptyHistoryAndReviewWithoutCallingLlm()
    {
        using var app = new TestApp();
        await app.SeedState(ProcessingState.FAILED, -1);
        await app.SeedState(ProcessingState.PENDING, 0);
        var client = app.CreateClient();
        Assert.Empty((await client.GetFromJsonAsync<Capture[]>("/api/captures", LlmJson.Options))!);
        var response = await client.PostAsync("/api/weekly-review", null);
        response.EnsureSuccessStatusCode();
        var review = await response.Content.ReadFromJsonAsync<WeeklyReview>();
        Assert.Empty(review!.KeyObservations);
        Assert.Null(review.SuggestedExperiment);
        Assert.Equal(0, app.Llm.ReviewCalls);
        Assert.Equal(2, (await app.Captures()).Count);
    }

    [Fact]
    public async Task ReviewIncludesOnlyRecentCompletedNotes()
    {
        using var app = new TestApp();
        await app.Seed(-8, -7, -1, 1);
        await app.SeedState(ProcessingState.FAILED, -2);
        await app.SeedState(ProcessingState.PENDING, 0);
        var response = await app.CreateClient().PostAsync("/api/weekly-review", null);
        response.EnsureSuccessStatusCode();
        Assert.Equal(2, app.Llm.ReviewCaptures.Count);
        Assert.All(app.Llm.ReviewCaptures, c => Assert.Equal(ProcessingState.COMPLETED, c.ProcessingState));
        var review = await response.Content.ReadFromJsonAsync<WeeklyReview>();
        Assert.Equal("Po tenisie więcej energii.", Assert.Single(review!.KeyObservations));
        app.Llm.Fail = true;
        Assert.Equal(HttpStatusCode.BadGateway, (await app.CreateClient().PostAsync("/api/weekly-review", null)).StatusCode);
    }

    [Fact]
    public async Task HistoryFiltersAndOrdersByCapturedTime()
    {
        using var app = new TestApp();
        await app.Seed(-8, -3, -1);
        await app.SeedState(ProcessingState.FAILED, 0);
        await app.SeedState(ProcessingState.PENDING, 0);
        var history = await app.CreateClient().GetFromJsonAsync<Capture[]>("/api/captures", LlmJson.Options);
        Assert.Equal(3, history!.Length);
        Assert.All(history, c => Assert.Equal(ProcessingState.COMPLETED, c.ProcessingState));
        var captures = await app.CreateClient().GetFromJsonAsync<Capture[]>("/api/captures?days=7&source=TEXT&limit=1", LlmJson.Options);
        Assert.Equal(TestApp.Now.AddDays(-1), Assert.Single(captures!).CapturedAt);
        var filtered = await app.CreateClient().GetFromJsonAsync<Capture[]>("/api/captures?type=TASK", LlmJson.Options);
        Assert.Empty(filtered!);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.CreateClient().GetAsync("/api/captures?source=FAX")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.CreateClient().GetAsync("/api/captures?limit=0")).StatusCode);
    }

    [Fact]
    public async Task UiRoutesAndFrameworkAssetsAreServed()
    {
        using var app = new TestApp();
        var client = app.CreateClient();
        foreach (var path in new[] { "/", "/tydzien", "/app.css", "/_framework/blazor.web.js" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEmpty(await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task SwaggerContainsAllThreeOperations()
    {
        using var app = new TestApp();
        var json = await app.CreateClient().GetStringAsync("/swagger/v1/swagger.json");
        Assert.Contains("/api/captures", json);
        Assert.Contains("/api/weekly-review", json);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var sourceSchema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("CaptureSource");
        Assert.Equal("string", sourceSchema.GetProperty("type").GetString());
        Assert.Contains(sourceSchema.GetProperty("enum").EnumerateArray(), item => item.GetString() == "VOICE");
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient().GetAsync("/swagger/index.html")).StatusCode);
    }
}

internal sealed class TestApp(int failOnSave = 0, string? postgres = null) : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-22T18:00:00Z");
    public FakeLlm Llm { get; } = new();
    private readonly string databaseName = Guid.NewGuid().ToString();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<LifeOsDbContext>();
            services.RemoveAll<DbContextOptions<LifeOsDbContext>>();
            services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<LifeOsDbContext>>();
            services.AddDbContext<LifeOsDbContext>(o =>
            {
                if (postgres is null) o.UseInMemoryDatabase(databaseName);
                else o.UseNpgsql(postgres);
                o.AddInterceptors(new FailSave(failOnSave));
            });
            services.RemoveAll<ILifeOsLlmService>();
            services.AddSingleton<ILifeOsLlmService>(Llm);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedClock());
        });
    }
    public async Task<List<Capture>> Captures()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LifeOsDbContext>().Captures.AsNoTracking().ToListAsync();
    }
    public Task Seed(params int[] offsets) => SeedState(ProcessingState.COMPLETED, offsets);
    public async Task SeedState(ProcessingState state, params int[] offsets)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LifeOsDbContext>();
        db.AddRange(offsets.Select(offset => new Capture
            { RawText = "Po tenisie dużo energii", Source = CaptureSource.TEXT, CreatedAt = Now,
                CapturedAt = Now.AddDays(offset), ProcessingState = state }));
        await db.SaveChangesAsync();
    }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class FailSave(int failOnSave) : SaveChangesInterceptor
    {
        private int calls;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (++calls == failOnSave) throw new DbUpdateException("Simulated database outage");
            return ValueTask.FromResult(result);
        }
    }
}

internal sealed class FakeLlm : ILifeOsLlmService
{
    public bool Fail { get; set; }
    public int ClassificationCalls { get; private set; }
    public int ReviewCalls { get; private set; }
    public Func<string, Task>? BeforeClassify { get; set; }
    public IReadOnlyList<Capture> ReviewCaptures { get; private set; } = [];
    public async Task<CaptureClassification> ClassifyCaptureAsync(string text, CancellationToken cancellationToken)
    {
        ClassificationCalls++;
        if (BeforeClassify is not null) await BeforeClassify(text);
        if (Fail) throw new LlmException("Usługa LLM jest niedostępna.");
        return new(CaptureType.FIELD_NOTE, "Spokój", "Medytacja przyniosła spokój.", ["medytacja"], false,
            new("medytacja", null, 7, null));
    }
    public Task<ReviewContent> GenerateWeeklyReviewAsync(IReadOnlyList<Capture> captures, CancellationToken cancellationToken)
    {
        ReviewCalls++;
        ReviewCaptures = captures;
        if (Fail) throw new LlmException("Usługa LLM jest niedostępna.");
        return Task.FromResult(ReviewContent.Empty with { KeyObservations = ["Po tenisie więcej energii."] });
    }
}
