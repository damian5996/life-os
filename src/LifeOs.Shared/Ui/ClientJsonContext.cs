using System.Text.Json.Serialization;
using LifeOs.Api.Models;

namespace LifeOs.Api.Ui;

// Explicit metadata also works in a trimmed Android Release build.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Capture[]))]
[JsonSerializable(typeof(Capture))]
[JsonSerializable(typeof(WeeklyReview))]
[JsonSerializable(typeof(TextCaptureInput))]
internal partial class ClientJsonContext : JsonSerializerContext;

internal sealed record TextCaptureInput(string Text, string Source);
