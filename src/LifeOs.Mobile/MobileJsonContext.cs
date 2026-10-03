using System.Text.Json.Serialization;
using LifeOs.Mobile.Models;

namespace LifeOs.Mobile;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ApiConfiguration))]
[JsonSerializable(typeof(CaptureResponse))]
internal partial class MobileJsonContext : JsonSerializerContext;
