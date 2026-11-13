using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using LifeOs.Api.Components.Pages;
using LifeOs.Api.Components.Shared;
using LifeOs.Api.Models;
using LifeOs.Api.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Api.Tests;

public sealed class UiTests
{
    [Fact]
    public void AdministrationTestsDraftWithoutSavingAndActivatesExplicitly()
    {
        var calls = new List<string>();
        var settings = new SettingsDocument(Guid.Empty, 0, Services.AiSettingsStore.Defaults(), Services.AiSettingsStore.Defaults());
        using var context = Context(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            calls.Add(request.Method + " " + path);
            if (path.EndsWith("/preview"))
            {
                var input = await request.Content!.ReadFromJsonAsync<PreviewSettingsRequest>(Llm.LlmJson.Options);
                Assert.Equal("Nowy styl", input!.Settings.CaptureInstructions);
                Assert.Equal("Przykład", input.Text);
                return Json(new SettingsPreview(new Capture { RawText = input.Text, Title = "Wynik próby", CategoryName = "Próbna" }, null));
            }
            if (request.Method == HttpMethod.Put)
            {
                var input = await request.Content!.ReadFromJsonAsync<SaveSettingsRequest>(Llm.LlmJson.Options);
                settings = settings with { Draft = input!.Draft, Revision = Guid.NewGuid() };
            }
            if (path.EndsWith("/activate")) settings = settings with { Active = settings.Draft, ActiveVersion = 1 };
            return Json(settings);
        });
        context.Services.AddSingleton(new UiEnvironment("Dev", true));
        var page = context.RenderComponent<Administration>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#capture-instructions")));
        page.Find("#capture-instructions").Input("Nowy styl");
        page.Find("#preview-text").Input("Przykład");
        page.Find("#preview-note").Click();
        page.WaitForAssertion(() => Assert.Contains("Wynik próby", page.Markup));
        Assert.DoesNotContain(calls, x => x.StartsWith("PUT") || x.EndsWith("/activate"));
        page.Find("#activate-settings").Click();
        page.WaitForAssertion(() => Assert.Contains("Aktywowano wersję 1", page.Markup));
        Assert.Equal(new[] { "PUT /api/administration/settings", "POST /api/administration/activate" }, calls.TakeLast(2));
    }

    [Theory]
    [InlineData("markdown", "text/markdown", ".md")]
    [InlineData("json", "application/json", ".json")]
    public void NativeHostUsesConfiguredApiAndSharesExport(string format, string contentType, string extension)
    {
        var exports = new FakeExportDestination();
        using var context = Context(request =>
        {
            Assert.Equal("https://phone-api.test/lifeos/", request.RequestUri!.GetLeftPart(UriPartial.Authority) + "/lifeos/");
            Assert.StartsWith("/lifeos/api/", request.RequestUri.AbsolutePath);
            if (request.RequestUri.AbsolutePath.EndsWith("/export"))
            {
                Assert.Equal($"?format={format}&type=TASK&deleted=true", request.RequestUri.Query);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent("Eksport żółć", System.Text.Encoding.UTF8, contentType) });
            }
            return Task.FromResult(Json(Array.Empty<Capture>()));
        });
        context.Services.AddSingleton(new LifeOsClientOptions(new Uri("https://phone-api.test/lifeos")));
        context.Services.AddSingleton<IExportDestination>(exports);
        var page = context.RenderComponent<Notes>();
        page.Find(".trash-toggle").Click();
        page.FindAll(".filters button").Single(b => b.TextContent == "Zadania").Click();
        Assert.Empty(page.FindAll(".export-actions a"));
        page.Find($".export-{format}").Click();
        page.WaitForAssertion(() => Assert.Single(exports.Files));
        var file = exports.Files.Single();
        Assert.Equal("Eksport żółć", System.Text.Encoding.UTF8.GetString(file.Content));
        Assert.EndsWith(extension, file.FileName);
        Assert.Equal(contentType, file.ContentType);
    }

    [Theory]
    [InlineData(503, "application/problem+json")]
    [InlineData(200, "text/html")]
    public void NativeExportFailureDoesNotShareAnErrorPage(int status, string contentType)
    {
        var exports = new FakeExportDestination();
        using var context = Context(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/export")
            ? new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("Error", System.Text.Encoding.UTF8, contentType) }
            : Json(Array.Empty<Capture>())));
        context.Services.AddSingleton<IExportDestination>(exports);
        var page = context.RenderComponent<Notes>();
        page.Find(".export-markdown").Click();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[role=alert]")));
        Assert.Empty(exports.Files);
        Assert.False(page.Find(".export-markdown").HasAttribute("disabled"));
    }

    [Fact]
    public async Task ReturningFromRecorderRefreshesListButKeepsTextDraft()
    {
        var calls = 0;
        var changes = new CaptureChanges();
        using var context = Context(_ => { calls++; return Task.FromResult(Json(Array.Empty<Capture>())); });
        context.Services.AddSingleton(changes);
        var page = context.RenderComponent<Notes>();
        await page.InvokeAsync(changes.Notify);
        page.WaitForAssertion(() => Assert.Equal(2, calls));
        page.Find(".add-button").Click();
        page.Find("textarea").Input("Niezapisany tekst");
        await page.InvokeAsync(changes.Notify);
        Assert.Equal(2, calls);
        Assert.Single(page.FindAll("textarea"));
        page.Dispose();
        changes.Notify();
        Assert.Equal(2, calls);
    }

    private sealed class FakeExportDestination : IExportDestination
    {
        public List<ExportFile> Files { get; } = [];
        public Task ShareAsync(ExportFile file) { Files.Add(file); return Task.CompletedTask; }
    }

    [Fact]
    public void ExportLinksFollowSelectedTypeAndTrashWithoutDownloadingOnRender()
    {
        var requests = new List<string>();
        using var context = Context(request =>
        {
            requests.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(Json(Array.Empty<Capture>()));
        });
        var page = context.RenderComponent<Notes>();
        Assert.Equal("api/captures/export?format=markdown", page.Find(".export-actions a").GetAttribute("href"));
        page.FindAll(".filters button").Single(b => b.TextContent == "Zadania").Click();
        Assert.Equal("api/captures/export?format=markdown&type=TASK", page.Find(".export-actions a").GetAttribute("href"));
        page.Find(".trash-toggle").Click();
        Assert.Equal("api/captures/export?format=json&deleted=true", page.Find(".export-actions a:last-of-type").GetAttribute("href"));
        page.FindAll(".filters button").Single(b => b.TextContent == "Pomysły").Click();
        Assert.Equal("api/captures/export?format=json&type=IDEA&deleted=true", page.Find(".export-actions a:last-of-type").GetAttribute("href"));
        Assert.All(page.FindAll(".export-actions a"), a =>
        {
            Assert.True(a.HasAttribute("download"));
            Assert.Equal("false", a.GetAttribute("data-enhance-nav"));
        });
        Assert.DoesNotContain(requests, path => path.Contains("/export"));
    }

    [Fact]
    public void DeleteAndRestoreRefreshTheListAndUseTheSelectedCaptureId()
    {
        var capture = Sample;
        using var context = Context(request =>
        {
            if (request.Method == HttpMethod.Delete)
            {
                Assert.Equal($"/api/captures/{capture.Id}", request.RequestUri!.AbsolutePath);
                capture.DeletedAt = DateTimeOffset.UtcNow;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal($"/api/captures/{capture.Id}/restore", request.RequestUri!.AbsolutePath);
                capture.DeletedAt = null;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            var trash = request.RequestUri!.Query.Contains("deleted=true");
            return Task.FromResult(Json(trash == (capture.DeletedAt is not null) ? new[] { capture } : []));
        });
        var page = context.RenderComponent<Notes>();
        page.Find(".capture-card").Click();
        page.Find(".delete-button").Click();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".capture-card")));
        Assert.Contains("Przeniesiono", page.Find("[role=status]").TextContent);
        page.Find(".trash-toggle").Click();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".capture-card")));
        page.Find(".capture-card").Click();
        page.Find(".restore-button").Click();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".capture-card")));
        page.Find(".trash-toggle").Click();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".capture-card")));
    }

    [Fact]
    public void DeleteFailurePreservesDetailsAndAllowsRetry()
    {
        using var context = Context(request => Task.FromResult(request.Method == HttpMethod.Get
            ? Json(new[] { Sample }) : Json(new { title = "Baza niedostępna." }, HttpStatusCode.ServiceUnavailable)));
        var page = context.RenderComponent<Notes>();
        page.Find(".capture-card").Click();
        page.Find(".delete-button").Click();
        page.WaitForAssertion(() => Assert.Contains("Baza niedostępna", page.Find("[role=alert]").TextContent));
        Assert.Equal(Sample.RawText, page.Find(".raw-text").TextContent);
        Assert.False(page.Find(".delete-button").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData("month")]
    [InlineData("six-months")]
    public void SelectingPeriodClearsOldReviewAndSendsSelectedValue(string period)
    {
        var paths = new List<string>();
        using var context = Context(request =>
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(Json(WeeklyReview.From(DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, ReviewContent.Empty)));
        });
        var page = context.RenderComponent<Week>();
        page.Find("button").Click();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".review-grid")));
        page.Find("select").Change(period);
        Assert.Empty(page.FindAll(".review-grid"));
        Assert.Single(paths);
        page.Find("button").Click();
        page.WaitForAssertion(() => Assert.Equal($"/api/review?period={period}", paths.Last()));
    }

    [Fact]
    public void CustomDatesAreSentAndReversedDatesAreBlocked()
    {
        var paths = new List<string>();
        using var context = Context(request =>
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(Json(WeeklyReview.From(DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, ReviewContent.Empty)));
        });
        var page = context.RenderComponent<Week>();
        page.Find("select").Change("custom");
        page.Find("#review-from").Change("2026-01-10");
        page.Find("#review-to").Change("2026-01-12");
        page.Find("button").Click();
        page.WaitForAssertion(() => Assert.Equal("/api/review?period=custom&from=2026-01-10&to=2026-01-12", Assert.Single(paths)));
        page.Find("#review-from").Change("2026-01-13");
        Assert.Empty(page.FindAll(".review-grid"));
        page.Find("button").Click();
        Assert.Single(paths);
        Assert.Contains("poprawne daty", page.Find("[role=alert]").TextContent);
    }

    private static Capture Sample => new()
    {
        RawText = "  Oryginał bez zmian\nżółć  ", Title = "Spokój po medytacji", Summary = "Chwila spokoju.",
        Source = CaptureSource.TEXT, Type = CaptureType.FIELD_NOTE, ProcessingState = ProcessingState.COMPLETED,
        CapturedAt = DateTimeOffset.Parse("2026-09-23T10:00:00Z"), Tags = ["medytacja", "spokój", "oddech", "czwarty"]
    };

    [Fact]
    public void ListRendersCardsWithoutRawTextAndOpensDetails()
    {
        using var context = Context(_ => Task.FromResult(Json(new[] { Sample })));
        var page = context.RenderComponent<Notes>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".capture-card")));
        Assert.Contains("Spokój po medytacji", page.Find(".card-title").TextContent);
        Assert.DoesNotContain("Oryginał bez zmian", page.Markup);
        Assert.Equal(3, page.FindAll(".tags span").Count);
        page.Find(".capture-card").Click();
        Assert.Equal(Sample.RawText, page.Find(".raw-text").TextContent);
        Assert.Contains("Podsumowanie AI", page.Find(".details").TextContent);
    }

    [Theory]
    [InlineData("Wszystkie", "")]
    [InlineData("Notatki terenowe", "&type=FIELD_NOTE")]
    [InlineData("Zadania", "&type=TASK")]
    [InlineData("Content", "&type=CONTENT_IDEA")]
    [InlineData("Pomysły", "&type=IDEA")]
    [InlineData("Inne", "&type=OTHER")]
    public void FiltersCallExistingApi(string label, string suffix)
    {
        var paths = new List<string>();
        using var context = Context(request =>
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(Json(Array.Empty<Capture>()));
        });
        var page = context.RenderComponent<Notes>();
        page.WaitForAssertion(() => Assert.Contains("Jeszcze tu cicho", page.Markup));
        page.FindAll(".filters button").Single(button => button.TextContent == label).Click();
        page.WaitForAssertion(() => Assert.Equal("/api/captures?limit=100" + suffix, paths.Last()));
    }

    [Theory]
    [InlineData(ProcessingState.COMPLETED)]
    [InlineData(ProcessingState.FAILED)]
    public void FormPostsTextUnchangedAndShowsSavedResult(ProcessingState state)
    {
        string? posted = null;
        using var context = Context(async request =>
        {
            if (request.Method == HttpMethod.Get) return Json(Array.Empty<Capture>());
            Assert.Equal("/api/captures", request.RequestUri!.AbsolutePath);
            posted = await request.Content!.ReadAsStringAsync();
            var capture = Sample;
            capture.ProcessingState = state;
            capture.ProcessingError = state == ProcessingState.FAILED ? "Usługa LLM jest niedostępna." : null;
            return Json(capture, HttpStatusCode.Created);
        });
        var page = context.RenderComponent<Notes>();
        page.Find(".add-button").Click();
        Assert.True(page.Find("button[type=submit]").HasAttribute("disabled"));
        page.Find("textarea").Input(Sample.RawText);
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.NotNull(posted));
        using var payload = JsonDocument.Parse(posted!);
        Assert.Equal("TEXT", payload.RootElement.GetProperty("source").GetString());
        Assert.Equal(Sample.RawText, payload.RootElement.GetProperty("text").GetString());
        Assert.False(payload.RootElement.TryGetProperty("capturedAt", out _));
        page.WaitForAssertion(() => Assert.Contains("Zapisano", page.Find("[role=status]").TextContent));
        if (state == ProcessingState.FAILED)
            Assert.Contains("nie pojawi się na liście", page.Find("[role=status]").TextContent);
    }

    [Fact]
    public void FailedRequestPreservesDraft()
    {
        var submittedTexts = new List<string>();
        using var context = Context(async request =>
        {
            if (request.Method == HttpMethod.Get) return Json(Array.Empty<Capture>());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            submittedTexts.Add(body.RootElement.GetProperty("text").GetString()!);
            return Json(new { title = "Baza niedostępna." }, HttpStatusCode.ServiceUnavailable);
        });
        var page = context.RenderComponent<Notes>();
        page.Find(".add-button").Click();
        page.Find("textarea").Input(Sample.RawText);
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Contains("Baza niedostępna", page.Find("[role=alert]").TextContent));
        Assert.False(page.Find("button[type=submit]").HasAttribute("disabled"));
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Equal(2, submittedTexts.Count));
        Assert.All(submittedTexts, value => Assert.Equal(Sample.RawText, value));
    }

    [Fact]
    public void WeeklyReviewRunsOnlyOnClickAndRendersSections()
    {
        var calls = 0;
        var review = WeeklyReview.From(DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow,
            ReviewContent.Empty with { KeyObservations = ["Tenis dodawał energii."], SuggestedExperiment = "Zapisz energię przed spacerem i po nim." });
        using var context = Context(request =>
        {
            calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/review?period=week", request.RequestUri!.PathAndQuery);
            return Task.FromResult(Json(review));
        });
        var page = context.RenderComponent<Week>();
        Assert.Equal(0, calls);
        page.Find("button").Click();
        page.WaitForAssertion(() => Assert.Equal(8, page.FindAll(".review-grid section").Count));
        Assert.Contains("Tenis dodawał energii.", page.Find(".review-grid").TextContent);
        Assert.Contains("Zapisz energię przed spacerem", page.Find(".experiment").TextContent);
        Assert.Contains("Brak wystarczających danych.", page.Find(".review-grid").TextContent);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void EmptyReviewDoesNotInventContent()
    {
        using var context = new TestContext();
        var view = context.RenderComponent<ReviewResult>(p => p.Add(x => x.Review,
            WeeklyReview.From(DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, ReviewContent.Empty)));
        Assert.Empty(view.FindAll("li"));
        Assert.Equal(8, view.FindAll("section p").Count);
        Assert.All(view.FindAll("section p"), p => Assert.Equal("Brak wystarczających danych.", p.TextContent));
    }

    private static TestContext Context(Func<HttpRequestMessage, Task<HttpResponseMessage>> action)
    {
        var context = new TestContext();
        context.Services.AddSingleton<IHttpClientFactory>(new StubFactory(action));
        context.Services.AddScoped<LifeOsApiClient>();
        return context;
    }
    private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(value) };
    private sealed class StubFactory(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(action));
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request);
    }
}
