using LifeOs.Api.Models;

namespace LifeOs.Api.Llm;

public interface ILifeOsLlmService
{
    Task<CaptureClassification> ClassifyCaptureAsync(string text, CancellationToken cancellationToken);
    Task<ReviewContent> GenerateWeeklyReviewAsync(IReadOnlyList<Capture> captures, CancellationToken cancellationToken);
}

public sealed class LlmException(string message) : Exception(message);

public sealed class LlmOptions
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public bool UseJsonSchema { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 60;
}
