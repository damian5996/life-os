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
            Assert.Equal("/api/weekly-review", request.RequestUri!.AbsolutePath);
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
