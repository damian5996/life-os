using System.Net.Http.Headers;
using System.Text.Json;
using LifeOs.Api.Models;
using LifeOs.Api.Prompts;
using Microsoft.Extensions.Options;

namespace LifeOs.Api.Llm;

public sealed class OpenAiCompatibleLlmService(HttpClient http, IOptions<LlmOptions> options) : ILifeOsLlmService
{
    public Task<CaptureClassification> ClassifyCaptureAsync(string text, CancellationToken cancellationToken) =>
        SendAsync<CaptureClassification>(LifeOsPrompts.Classification, text, "capture", cancellationToken);

    public Task<ReviewContent> GenerateWeeklyReviewAsync(IReadOnlyList<Capture> captures, CancellationToken cancellationToken) =>
        SendAsync<ReviewContent>(LifeOsPrompts.WeeklyReview, JsonSerializer.Serialize(captures.Select(c => new
        {
            c.Id, c.CapturedAt, c.RawText, c.Type, c.Title, c.Summary, c.Tags, c.ActionRequired
        }), LlmJson.Options), "weekly_review", cancellationToken);

    private async Task<T> SendAsync<T>(string prompt, string input, string name, CancellationToken cancellationToken)
    {
        var config = options.Value;
        if (string.IsNullOrWhiteSpace(config.Model)) throw new LlmException("Nie skonfigurowano modelu LLM.");
        var schema = LlmJson.Schema<T>();
        object format = config.UseJsonSchema
            ? new { type = "json_schema", json_schema = new { name, strict = true, schema } }
            : new { type = "json_object" };
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(config.BaseUrl.TrimEnd('/') + "/"), "chat/completions"));
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Content = JsonContent.Create(new
        {
            model = config.Model,
            messages = new[]
            {
                new { role = "system", content = prompt + "\nSchemat JSON:\n" + schema.ToJsonString() },
                new { role = "user", content = input }
            },
            response_format = format
        });
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new LlmException($"Usługa LLM zwróciła HTTP {(int)response.StatusCode}.");
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            var choice = body.RootElement.GetProperty("choices")[0];
            if (choice.GetProperty("finish_reason").GetString() != "stop")
                throw new LlmException("Model nie ukończył odpowiedzi.");
            var message = choice.GetProperty("message");
            if (message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind != JsonValueKind.Null)
                throw new LlmException("Model odmówił przetworzenia notatki.");
            return LlmJson.Parse<T>(message.GetProperty("content").GetString() ?? "null");
        }
        catch (HttpRequestException) { throw new LlmException("Usługa LLM jest niedostępna."); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new LlmException("Przekroczono czas odpowiedzi LLM."); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        { throw new LlmException("Usługa LLM zwróciła nieprawidłową odpowiedź."); }
    }
}
