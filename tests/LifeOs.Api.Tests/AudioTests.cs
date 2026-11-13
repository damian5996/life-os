using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LifeOs.Api.Audio;
using LifeOs.Api.Endpoints;
using LifeOs.Api.Llm;
using LifeOs.Api.Models;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LifeOs.Api.Tests;

public sealed class AudioTests
{
    [Theory]
    [InlineData("Czuję zmęczenie. 음. No nic.")]
    [InlineData("어 피사 찍다래요.")]
    [InlineData("Hmm ᄋ ㅓ ﾡ")]
    public async Task HangulRetriesOriginalAudioWithFallbackAndNoPrompt(string suspect)
    {
        var calls = 0;
        var original = new byte[] { 1, 2, 3, 4 };
        using var source = new MemoryStream(original);
        using var http = new HttpClient(new Handler(async request =>
        {
            var fields = Assert.IsType<MultipartFormDataContent>(request.Content)
                .ToDictionary(c => c.Headers.ContentDisposition!.Name!.Trim('"'));
            Assert.Equal(original, await fields["file"].ReadAsByteArrayAsync());
            Assert.Equal("pl", await fields["language"].ReadAsStringAsync());
            Assert.False(fields.ContainsKey("prompt"));
            Assert.Equal(calls++ == 0 ? "gpt-4o-mini-transcribe" : "gpt-4o-transcribe", await fields["model"].ReadAsStringAsync());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { text = calls == 1 ? suspect : "Czuję zmęczenie. Hmm, no nic." }) };
        }));
        var service = new OpenAiAudioTranscriptionService(http, Options.Create(new LlmOptions()));
        Assert.Equal("Czuję zmęczenie. Hmm, no nic.", await service.TranscribeAsync(source, "capture.m4a", "audio/mp4", default));
        Assert.Equal(2, calls);
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task NonSeekableAudioIsReplayedWithoutLosingBytes()
    {
        using var compressed = new MemoryStream();
        using (var writer = new System.IO.Compression.GZipStream(compressed, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
            writer.Write([1, 2, 3]);
        compressed.Position = 0;
        using var source = new System.IO.Compression.GZipStream(compressed, System.IO.Compression.CompressionMode.Decompress);
        var calls = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            var fields = Assert.IsType<MultipartFormDataContent>(request.Content).ToDictionary(c => c.Headers.ContentDisposition!.Name!.Trim('"'));
            Assert.Equal(new byte[] { 1, 2, 3 }, await fields["file"].ReadAsByteArrayAsync());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { text = ++calls == 1 ? "음" : "Hmm, zastanawiam się." }) };
        }));
        var service = new OpenAiAudioTranscriptionService(http, Options.Create(new LlmOptions()));
        Assert.Equal("Hmm, zastanawiam się.", await service.TranscribeAsync(source, "capture.m4a", "audio/mp4", default));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task TwoSuspectResultsCreateNoNoteAndDoNotReachClassification()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { text = "Polska mowa. 음..." }) });
        }));
        using var app = new TestApp();
        using var configured = app.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAudioTranscriptionService>();
            services.AddSingleton<IAudioTranscriptionService>(new OpenAiAudioTranscriptionService(http, Options.Create(new LlmOptions())));
        }));
        using var upload = Upload();
        var response = await configured.CreateClient().PostAsync("/api/captures/audio", upload);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(2, calls);
        using var scope = configured.Services.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<LifeOs.Api.Data.LifeOsDbContext>().Captures);
        Assert.Equal(0, app.Llm.ClassificationCalls);
    }

    [Theory]
    [InlineData("ko", "음...")]
    [InlineData("pl", "Mhm, yhm, hmm… .NET, Azure, coaching. Łódź.")]
    public async Task CleanPolishOrExplicitKoreanDoesNotRetry(string language, string text)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { text }) });
        }));
        var service = new OpenAiAudioTranscriptionService(http, Options.Create(new LlmOptions { TranscriptionLanguage = language }));
        Assert.Equal(text, await service.TranscribeAsync(new MemoryStream([1]), "capture.m4a", "audio/mp4", default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task UnavailableFallbackDoesNotAcceptSuspectFirstResult()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ => Task.FromResult(++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { text = "음" }) }
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        var service = new OpenAiAudioTranscriptionService(http, Options.Create(new LlmOptions()));
        var error = await Assert.ThrowsAsync<TranscriptionException>(() => service.TranscribeAsync(new MemoryStream([1]), "capture.m4a", "audio/mp4", default));
        Assert.False(error.NoSpeech);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(null, 0, "audio/mp4", 400)]
    [InlineData("note.m4a", 0, "audio/mp4", 400)]
    [InlineData("note.3gp", 4, "audio/3gpp", 415)]
    [InlineData("note.exe", 4, "audio/mp4", 415)]
    [InlineData("note.m4a", 4, "text/plain", 415)]
    [InlineData("note.m4a", 25_000_001, "audio/mp4", 413)]
    [InlineData("note.m4a", 25_070_000, "audio/mp4", 413)]
    public async Task InvalidFilesNeverReachTranscription(string? name, int bytes, string mime, int status)
    {
        using var app = new TestApp();
        using var form = Upload(name, bytes, mime);
        var response = await app.CreateClient().PostAsync("/api/captures/audio", form);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(0, app.Transcription.Calls);
        Assert.Empty(await app.Captures());
    }

    [Theory]
    [InlineData(".m4a", "audio/mp4")]
    [InlineData(".mp4", "video/mp4")]
    [InlineData(".mp3", "audio/mpeg")]
    [InlineData(".wav", "audio/wav")]
    [InlineData(".webm", "audio/webm")]
    [InlineData(".ogg", "audio/ogg")]
    [InlineData(".M4A", "application/octet-stream")]
    public async Task AudioUsesRawFirstClassificationPipeline(string extension, string mime)
    {
        using var app = new TestApp();
        app.Llm.BeforeClassify = async text =>
        {
            var pending = Assert.Single(await app.Captures());
            Assert.Equal(ProcessingState.PENDING, pending.ProcessingState);
            Assert.Equal(app.Transcription.Text, text);
            Assert.Equal(text, pending.RawText);
            Assert.Equal(CaptureSource.VOICE, pending.Source);
        };
        using var form = Upload("private-name" + extension, 4, mime);
        var response = await app.CreateClient().PostAsync("/api/captures/audio", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<Capture>(LlmJson.Options);
        var saved = Assert.Single(await app.Captures());
        Assert.Equal(saved.Id, result!.Id);
        Assert.Equal(ProcessingState.COMPLETED, saved.ProcessingState);
        Assert.Equal("Spokój", saved.Title);
        Assert.Equal("medytacja", Assert.Single(saved.Tags));
        Assert.Equal(1, app.Llm.ClassificationCalls);
        Assert.Equal(1, app.Transcription.Calls);
        Assert.Equal("capture" + extension.ToLowerInvariant(), app.Transcription.FileName);
    }

    [Theory]
    [InlineData(true, "text")]
    [InlineData(false, "")]
    [InlineData(false, " \r\n ")]
    public async Task FailedOrBlankTranscriptionCreatesNothing(bool fail, string text)
    {
        using var app = new TestApp();
        app.Transcription.Fail = fail;
        app.Transcription.Text = text;
        using var form = Upload();
        var response = await app.CreateClient().PostAsync("/api/captures/audio", form);
        Assert.Equal(fail ? HttpStatusCode.BadGateway : HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(await app.Captures());
        Assert.Equal(0, app.Llm.ClassificationCalls);
    }

    [Fact]
    public async Task ClassificationFailurePreservesTranscription()
    {
        using var app = new TestApp();
        app.Llm.Fail = true;
        using var form = Upload();
        var response = await app.CreateClient().PostAsync("/api/captures/audio", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = Assert.Single(await app.Captures());
        Assert.Equal(ProcessingState.FAILED, saved.ProcessingState);
        Assert.Equal(app.Transcription.Text, saved.RawText);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task DatabaseFailureUsesExistingFailureContract(int save)
    {
        using var app = new TestApp(save);
        using var form = Upload();
        var response = await app.CreateClient().PostAsync("/api/captures/audio", form);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        if (save == 1) Assert.Empty(await app.Captures());
        else
        {
            Assert.Equal(app.Transcription.Text, Assert.Single(await app.Captures()).RawText);
            Assert.Contains("rawCaptureSaved", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task WrongFieldAndMultipleFilesRejected()
    {
        using var app = new TestApp();
        using var wrong = new MultipartFormDataContent();
        wrong.Add(new ByteArrayContent([1]), "audio", "note.m4a");
        Assert.Equal(HttpStatusCode.BadRequest, (await app.CreateClient().PostAsync("/api/captures/audio", wrong)).StatusCode);
        using var multiple = Upload();
        multiple.Add(new ByteArrayContent([1]), "file", "extra.m4a");
        Assert.Equal(HttpStatusCode.BadRequest, (await app.CreateClient().PostAsync("/api/captures/audio", multiple)).StatusCode);
        Assert.Equal(0, app.Transcription.Calls);
    }

    internal static MultipartFormDataContent Upload(string? name = "note.m4a", int bytes = 4, string mime = "audio/mp4")
    {
        var form = new MultipartFormDataContent();
        if (name is not null)
        {
            var file = new ByteArrayContent(new byte[bytes]);
            file.Headers.ContentType = new MediaTypeHeaderValue(mime);
            form.Add(file, "file", name);
        }
        return form;
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("{\"text\":5}")]
    [InlineData("{\"text\":\" \"}")]
    public async Task AdapterRejectsInvalidResponses(string json)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json) })));
        var service = new OpenAiAudioTranscriptionService(http, Options.Create(new LlmOptions()));
        await Assert.ThrowsAsync<TranscriptionException>(() => service.TranscribeAsync(new MemoryStream([1]), "capture.m4a", "audio/mp4", default));
    }

    [Fact]
    public async Task AdapterSendsPolishMultipartWithExistingKeyAndConfiguredModel()
    {
        using var http = new HttpClient(new Handler(async request =>
        {
            Assert.Equal("https://provider.test/v1/audio/transcriptions", request.RequestUri!.ToString());
            Assert.Equal("test-key", request.Headers.Authorization!.Parameter);
            var form = Assert.IsType<MultipartFormDataContent>(request.Content);
            var fields = form.ToDictionary(c => c.Headers.ContentDisposition!.Name!.Trim('"'));
            Assert.Equal("my-transcriber", await fields["model"].ReadAsStringAsync());
            Assert.Equal("pl", await fields["language"].ReadAsStringAsync());
            Assert.Equal("json", await fields["response_format"].ReadAsStringAsync());
            Assert.Equal("auto", await fields["chunking_strategy"].ReadAsStringAsync());
            Assert.False(fields.ContainsKey("prompt"));
            Assert.Equal(new byte[] { 1, 2 }, await fields["file"].ReadAsByteArrayAsync());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"text\":\"Backend w Azure.\"}") };
        }));
        var service = new OpenAiAudioTranscriptionService(http, Options.Create(new LlmOptions
            { BaseUrl = "https://provider.test/v1", ApiKey = "test-key", TranscriptionModel = "my-transcriber" }));
        Assert.Equal("Backend w Azure.", await service.TranscribeAsync(new MemoryStream([1, 2]), "capture.m4a", "audio/mp4", default));
    }

    [Fact]
    public async Task ProviderFailureNeverLeaksBody()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            { Content = new StringContent("private upstream data") })));
        var service = new OpenAiAudioTranscriptionService(http, Options.Create(new LlmOptions()));
        var error = await Assert.ThrowsAsync<TranscriptionException>(() => service.TranscribeAsync(new MemoryStream([1]), "capture.m4a", "audio/mp4", default));
        Assert.DoesNotContain("private", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n ")]
    public async Task AdapterReportsNoSpeechSeparatelyFromProviderFailure(string text)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new { text }) })));
        var service = new OpenAiAudioTranscriptionService(http, Options.Create(new LlmOptions()));
        var error = await Assert.ThrowsAsync<TranscriptionException>(() =>
            service.TranscribeAsync(new MemoryStream([1]), "capture.m4a", "audio/mp4", default));
        Assert.True(error.NoSpeech);
        Assert.Contains("Notatka nie została utworzona", error.Message);
    }

    [Theory]
    [InlineData("Tak.")]
    [InlineData("  Cicho mówię o .NET i Azure.  ")]
    public async Task AdapterPreservesShortAndTechnicalSpeech(string text)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new { text }) })));
        var service = new OpenAiAudioTranscriptionService(http, Options.Create(new LlmOptions()));
        Assert.Equal(text, await service.TranscribeAsync(new MemoryStream([1]), "capture.m4a", "audio/mp4", default));
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request);
    }
}

internal sealed class FakeTranscription : IAudioTranscriptionService
{
    public string Text { get; set; } = "  Dzisiaj backend w Azure.  ";
    public bool Fail { get; set; }
    public int Calls { get; private set; }
    public string? FileName { get; private set; }
    public Task<string> TranscribeAsync(Stream stream, string name, string? mime, CancellationToken cancellationToken)
    {
        Calls++;
        FileName = name;
        if (Fail) throw new TranscriptionException("Usługa transkrypcji jest niedostępna.");
        return Task.FromResult(Text);
    }
}
