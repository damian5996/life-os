using System.Net;
using LifeOs.Mobile.Services;

namespace LifeOs.Mobile.Tests;

public sealed class UploadTests
{
    [Theory]
    [InlineData("COMPLETED")]
    [InlineData("FAILED")]
    public async Task SendsFileAndAcceptsBothSavedStates(string state)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            var uploaded = false;
            using var http = new HttpClient(new Handler(async request =>
            {
                Assert.Equal("https://lifeos.test/api/captures/audio", request.RequestUri!.ToString());
                Assert.Null(request.Headers.Authorization);
                var form = Assert.IsType<MultipartFormDataContent>(request.Content);
                var file = Assert.Single(form);
                Assert.Equal("file", file.Headers.ContentDisposition!.Name!.Trim('"'));
                Assert.Equal("audio/mp4", file.Headers.ContentType!.MediaType);
                Assert.Equal(new byte[] { 1, 2, 3 }, await file.ReadAsByteArrayAsync());
                Assert.True(uploaded);
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent($"{{\"id\":\"f4112794-1287-4411-a3ed-1a1697ca9c00\",\"processingState\":\"{state}\"}}")
                };
            }));
            var api = new LifeOsApiClient(http, new("https://lifeos.test/"));
            var result = await api.UploadAudioAsync(path, () => uploaded = true, default);
            Assert.Equal(state, result.ProcessingState);
            Assert.True(File.Exists(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(503, "private error")]
    [InlineData(200, "{}")]
    [InlineData(201, "{}")]
    [InlineData(201, "{\"id\":\"f4112794-1287-4411-a3ed-1a1697ca9c00\",\"processingState\":\"PENDING\"}")]
    public async Task ErrorOrInvalidConfirmationLeavesFile(int status, string body)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, [1]);
            using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
                { Content = new StringContent(body) })));
            var api = new LifeOsApiClient(http, new("https://lifeos.test/"));
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => api.UploadAudioAsync(path, () => { }, default));
            Assert.DoesNotContain("private error", error.Message);
            Assert.True(File.Exists(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EmptyLocalFileIsNotSent()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var http = new HttpClient(new Handler(_ => throw new Exception("Must not upload")));
            var api = new LifeOsApiClient(http, new("https://lifeos.test/"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => api.UploadAudioAsync(path, () => { }, default));
        }
        finally { File.Delete(path); }
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request);
    }
}
