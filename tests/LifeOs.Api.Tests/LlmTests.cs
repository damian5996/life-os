using System.Net;
using System.Text.Json;
using LifeOs.Api.Llm;
using LifeOs.Api.Models;
using Microsoft.Extensions.Options;

namespace LifeOs.Api.Tests;

public sealed class LlmTests
{
    private const string Valid = """
        {"type":"FIELD_NOTE","title":"Spokój po medytacji","summary":"Po 15 minutach spokojniej.",
        "tags":["medytacja","spokój"],"actionRequired":false,
        "metadata":{"activity":"medytacja","energy":null,"satisfaction":7,"location":null}}
        """;

    [Theory]
    [InlineData("FIELD_NOTE")] [InlineData("TASK")] [InlineData("CONTENT_IDEA")]
    [InlineData("IDEA")] [InlineData("OTHER")]
    public void CategoriesAndPolishResponseDeserialize(string type)
    {
        var result = LlmJson.Parse<CaptureClassification>(Valid.Replace("FIELD_NOTE", type));
        Assert.Equal(Enum.Parse<CaptureType>(type), result.Type);
        Assert.Equal("Spokój po medytacji", result.Title);
        Assert.Equal(7, result.Metadata.Satisfaction);
        Assert.Null(result.Metadata.Energy);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("Oto odpowiedź: {}")]
    public void RejectsIncompleteOrUnstructuredOutput(string json) =>
        Assert.Throws<LlmException>(() => LlmJson.Parse<CaptureClassification>(json));

    [Fact]
    public void RejectsUnknownCategoriesAndNullFields()
    {
        Assert.Throws<LlmException>(() => LlmJson.Parse<CaptureClassification>(Valid.Replace("FIELD_NOTE", "DIAGNOSIS")));
        Assert.Throws<LlmException>(() => LlmJson.Parse<CaptureClassification>(Valid.Replace("\"Spokój po medytacji\"", "null")));
        Assert.Throws<LlmException>(() => LlmJson.Parse<CaptureClassification>(Valid.Replace("[\"medytacja\",\"spokój\"]", "[null]")));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task HttpAdapterUsesConfiguredEndpointAndStructuredJson(bool strict)
    {
        using var client = new HttpClient(new StubHandler(async request =>
        {
            Assert.Equal("https://example.test/v1/chat/completions", request.RequestUri!.ToString());
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("wybrany-model", payload.RootElement.GetProperty("model").GetString());
            Assert.Equal("  żółć  ", payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
            Assert.Equal(strict ? "json_schema" : "json_object", payload.RootElement.GetProperty("response_format").GetProperty("type").GetString());
            return Envelope(Valid);
        }));
        var llm = Create(client, strict);
        Assert.Equal(CaptureType.FIELD_NOTE, (await llm.ClassifyCaptureAsync("  żółć  ", default)).Type);
    }

    [Theory]
    [InlineData("length", null)] [InlineData("stop", "Nie")]
    public async Task RefusedAndTruncatedResponsesFail(string finish, string? refusal)
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(Envelope(Valid, finish, refusal))));
        await Assert.ThrowsAsync<LlmException>(() => Create(client).ClassifyCaptureAsync("tekst", default));
    }

    [Fact]
    public async Task ProviderErrorsDoNotLeakResponseBody()
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            { Content = new StringContent("sensitive upstream content") })));
        var error = await Assert.ThrowsAsync<LlmException>(() => Create(client).ClassifyCaptureAsync("tekst", default));
        Assert.DoesNotContain("sensitive", error.Message);
    }

    [Fact]
    public void StrictSchemasCloseObjectsAndRequireAllProperties()
    {
        Check(LlmJson.Schema<CaptureClassification>());
        Check(LlmJson.Schema<ReviewContent>());
        static void Check(System.Text.Json.Nodes.JsonNode? node)
        {
            if (node is System.Text.Json.Nodes.JsonObject obj)
            {
                if (obj["properties"] is System.Text.Json.Nodes.JsonObject props)
                {
                    Assert.False(obj["additionalProperties"]!.GetValue<bool>());
                    Assert.Equal(props.Count, obj["required"]!.AsArray().Count);
                }
                foreach (var child in obj) Check(child.Value);
            }
            else if (node is System.Text.Json.Nodes.JsonArray array)
                foreach (var child in array) Check(child);
        }
    }

    private static OpenAiCompatibleLlmService Create(HttpClient client, bool strict = true) => new(client,
        Options.Create(new LlmOptions { BaseUrl = "https://example.test/v1", Model = "wybrany-model", UseJsonSchema = strict }));
    private static HttpResponseMessage Envelope(string content, string finish = "stop", string? refusal = null) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            { choices = new[] { new { finish_reason = finish, message = new { content, refusal } } } })) };
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request);
    }
}
