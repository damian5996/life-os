using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeOs.Api.Data;
using LifeOs.Api.Llm;
using LifeOs.Api.Models;
using LifeOs.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Api.Tests;

public sealed class AdministrationTests
{
    [Fact]
    public async Task CustomCategoryFiltersAndExportsPreserveHistoricalNameAfterSettingsChange()
    {
        using var app = new TestApp();
        var id = Guid.NewGuid();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LifeOsDbContext>();
            db.Captures.Add(new Capture { RawText = "Test kategorii", Source = CaptureSource.TEXT, Type = CaptureType.OTHER,
                CategoryId = id, CategoryName = "Dawna nazwa", ProcessingState = ProcessingState.COMPLETED,
                CapturedAt = TestApp.Now, CreatedAt = TestApp.Now });
            db.Captures.Add(new Capture { RawText = "Poza filtrem", Source = CaptureSource.TEXT, ProcessingState = ProcessingState.COMPLETED,
                CapturedAt = TestApp.Now, CreatedAt = TestApp.Now });
            await db.SaveChangesAsync();
            var settings = AiSettingsStore.Defaults();
            settings.Categories.Add(new() { Id = id, Name = "Nowa nazwa", Instructions = "Opis", Enabled = false });
            await new AiSettingsStore(db).SaveAsync(Guid.Empty, settings, true, default);
        }
        var client = app.CreateClient();
        var captures = await client.GetFromJsonAsync<Capture[]>($"/api/captures?categoryId={id}", LlmJson.Options);
        Assert.Equal("Dawna nazwa", Assert.Single(captures!).CategoryName);
        using var export = JsonDocument.Parse(await client.GetStringAsync($"/api/captures/export?format=json&categoryId={id}"));
        Assert.Equal(1, export.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(id, export.RootElement.GetProperty("scope").GetProperty("categoryId").GetGuid());
    }

    [Fact]
    public async Task DraftIsIsolatedUntilActivationAndStaleSavesAreRejected()
    {
        var options = new DbContextOptionsBuilder<LifeOsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new LifeOsDbContext(options);
        var store = new AiSettingsStore(db);
        var original = await store.ReadAsync(default);
        original.Draft.Categories[0].Name = "Mój dziennik";
        var saved = (await store.SaveAsync(original.Revision, original.Draft, false, default))!;
        Assert.NotEqual(saved.Draft.Categories[0].Name, saved.Active.Categories[0].Name);
        Assert.Equal(0, saved.ActiveVersion);
        await using var second = new LifeOsDbContext(options);
        Assert.Null(await new AiSettingsStore(second).SaveAsync(original.Revision, original.Draft, false, default));
        var activated = (await store.SaveAsync(saved.Revision, saved.Draft, true, default))!;
        Assert.Equal(1, activated.ActiveVersion);
        Assert.Equal("Mój dziennik", activated.Active.Categories[0].Name);
        await using var reopened = new LifeOsDbContext(options);
        Assert.Equal("Mój dziennik", (await new AiSettingsStore(reopened).ReadAsync(default)).Active.Categories[0].Name);
    }
    [Fact]
    public void InvalidSettingsCannotBeActivated()
    {
        var config = AiSettingsStore.Defaults();
        Assert.Null(AiSettingsStore.Validate(config));
        config.Categories.ForEach(c => c.Enabled = false);
        Assert.NotNull(AiSettingsStore.Validate(config));
        config = AiSettingsStore.Defaults();
        config.Categories[1].Name = config.Categories[0].Name.ToUpperInvariant();
        Assert.NotNull(AiSettingsStore.Validate(config));
        config = AiSettingsStore.Defaults();
        config.CaptureInstructions = "";
        Assert.NotNull(AiSettingsStore.Validate(config));
    }
    [Theory]
    [InlineData("Production", "Host=127.0.0.1;Port=55439;Database=lifeos_dev;Username=lifeos_dev")]
    [InlineData("Development", "Host=remote.example;Port=55439;Database=lifeos_dev;Username=lifeos_dev")]
    [InlineData("Development", "Host=localhost;Port=5435;Database=lifeos;Username=lifeos")]
    public void DevRefusesProductionOrSharedDatabase(string environment, string connection) =>
        Assert.Throws<InvalidOperationException>(() => LocalDevGuard.Validate(environment, connection));

    [Fact]
    public async Task AdministrationIsNotExposedByProduction()
    {
        using var app = new TestApp();
        Assert.Equal(HttpStatusCode.NotFound, (await app.CreateClient().GetAsync("/api/administration/settings")).StatusCode);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomClassificationUsesOnlyEnabledIdsAndValidatesModelChoice(bool invalid)
    {
        var config = AiSettingsStore.Defaults();
        config.Categories.ForEach(c => c.Enabled = false);
        var custom = new NoteCategory { Name = "Moja firma", Instructions = "Notatki o prowadzeniu firmy." };
        config.Categories.Add(custom);
        using var http = new HttpClient(new Handler(async request =>
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var ids = payload.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema")
                .GetProperty("properties").GetProperty("categoryId").GetProperty("enum").EnumerateArray().ToArray();
            Assert.Equal(custom.Id.ToString(), Assert.Single(ids).GetString());
            var content = JsonSerializer.Serialize(new ConfiguredClassification(invalid ? Guid.NewGuid().ToString() : custom.Id.ToString(),
                "Plan firmy", "Chcesz rozwinąć firmę.", ["firma"], true, new(null, null, null, null)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { choices = new[] { new { finish_reason = "stop", message = new { content } } } }) };
        }));
        var llm = new OpenAiCompatibleLlmService(http, Options.Create(new LlmOptions { Model = "test" }));
        if (invalid) await Assert.ThrowsAsync<LlmException>(() => llm.ClassifyWithSettingsAsync(config, 3, "Firma", default));
        else
        {
            var result = await llm.ClassifyWithSettingsAsync(config, 3, "Firma", default);
            Assert.Equal(custom.Id, result.CategoryId);
            Assert.Equal("Moja firma", result.CategoryName);
            Assert.Equal(3, result.SettingsVersion);
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => handler(request);
    }
}

