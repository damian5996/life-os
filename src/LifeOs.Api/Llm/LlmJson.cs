using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Schema;
using LifeOs.Api.Models;

namespace LifeOs.Api.Llm;

public static class LlmJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
    public static System.Text.Json.Nodes.JsonNode Schema<T>() => Options.GetJsonSchemaAsNode(typeof(T),
        new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (_, node) =>
            {
                if (node is System.Text.Json.Nodes.JsonObject obj && obj.ContainsKey("properties"))
                    obj["additionalProperties"] = false;
                return node;
            }
        });
    public static T Parse<T>(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException();
            if (result is CaptureClassification c &&
                (!Enum.IsDefined(c.Type) || string.IsNullOrWhiteSpace(c.Title) ||
                 string.IsNullOrWhiteSpace(c.Summary) || c.Tags.Length is < 1 or > 5 ||
                 c.Tags.Any(string.IsNullOrWhiteSpace))) throw new JsonException();
            if (result is ReviewContent r && new[] { r.KeyObservations, r.RecurringThemes,
                    r.EnergyGivers, r.EnergyDrainers, r.OpenTasks, r.BestContentIdeas, r.Patterns }
                .Any(items => items.Any(string.IsNullOrWhiteSpace))) throw new JsonException();
            return result;
        }
        catch (JsonException) { throw new LlmException("Model zwrócił nieprawidłową odpowiedź JSON."); }
    }
}
