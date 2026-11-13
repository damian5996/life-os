using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOs.Api.Models;
using LifeOs.Api.Ui;

namespace LifeOs.Api.Services;

public static class CaptureExport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static byte[] ToJson(IReadOnlyList<Capture> captures, DateTimeOffset exportedAt, string? type, bool deleted, Guid? categoryId = null) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, exportedAt, scope = new { type, deleted, categoryId }, count = captures.Count, captures
        }, Json);

    public static byte[] ToMarkdown(IReadOnlyList<Capture> captures, DateTimeOffset exportedAt, string? type, bool deleted, Guid? categoryId = null)
    {
        var output = new StringBuilder();
        output.AppendLine("# Life OS — eksport notatek").AppendLine();
        output.AppendLine($"Eksport: {PolishLabels.Date(exportedAt)} (Europe/Warsaw)");
        output.AppendLine($"Zakres: {(deleted ? "kosz" : "notatki poza koszem")}; typ: {(type is null ? "wszystkie" : PolishLabels.Type(Enum.Parse<CaptureType>(type)))}");
        output.AppendLine($"Liczba notatek: {captures.Count}").AppendLine();
        if (categoryId.HasValue) output.AppendLine($"Filtr kategorii: {categoryId}").AppendLine();
        if (captures.Count == 0) output.AppendLine("Brak notatek w wybranym zakresie.");
        foreach (var capture in captures)
        {
            output.AppendLine("---").AppendLine();
            output.AppendLine($"## {Escape(capture.Title ?? "Notatka bez tytułu").ReplaceLineEndings(" ")}").AppendLine();
            output.AppendLine($"- ID: {capture.Id}");
            output.AppendLine($"- Data notatki: {PolishLabels.Date(capture.CapturedAt)} (Europe/Warsaw)");
            output.AppendLine($"- Data zapisu: {PolishLabels.Date(capture.CreatedAt)} (Europe/Warsaw)");
            output.AppendLine($"- Typ: {Escape(capture.CategoryName ?? PolishLabels.Type(capture.Type))}");
            output.AppendLine($"- Źródło: {PolishLabels.Source(capture.Source)}");
            output.AppendLine($"- Stan AI: {PolishLabels.State(capture.ProcessingState)}");
            output.AppendLine($"- Wymaga działania: {capture.ActionRequired switch { true => "tak", false => "nie", null => "nie ustalono" }}");
            output.AppendLine($"- Tagi: {(capture.Tags.Length == 0 ? "brak" : string.Join(", ", capture.Tags.Select(t => Escape(t).ReplaceLineEndings(" "))))}");
            if (capture.DeletedAt is { } removed)
                output.AppendLine($"- Przeniesiono do kosza: {PolishLabels.Date(removed)} (Europe/Warsaw)");
            output.AppendLine().AppendLine("### Oryginalna treść").AppendLine();
            AppendLiteral(output, capture.RawText, "text");
            output.AppendLine("### Podsumowanie AI").AppendLine();
            output.AppendLine(capture.Summary is null ? "Brak podsumowania." : Escape(capture.Summary)).AppendLine();
            if (capture.ProcessingError is not null)
                output.AppendLine($"Błąd przetwarzania: {Escape(capture.ProcessingError)}").AppendLine();
            output.AppendLine("### Metadane").AppendLine();
            AppendLiteral(output, capture.MetadataJson, "json");
        }
        return Encoding.UTF8.GetBytes(output.ToString());
    }

    // Keep the original text literal, even when it contains Markdown fences or HTML.
    private static void AppendLiteral(StringBuilder output, string value, string language)
    {
        var longestRun = 0;
        var run = 0;
        foreach (var c in value)
        {
            run = c == '`' ? run + 1 : 0;
            longestRun = Math.Max(longestRun, run);
        }
        var fence = new string('`', Math.Max(3, longestRun + 1));
        output.AppendLine(fence + language).Append(value);
        if (!value.EndsWith('\n')) output.AppendLine();
        output.AppendLine(fence).AppendLine();
    }

    private static string Escape(string value)
    {
        var output = new StringBuilder();
        foreach (var c in value)
        {
            if (c == '&') output.Append("&amp;");
            else if (c == '<') output.Append("&lt;");
            else if (c == '>') output.Append("&gt;");
            else
            {
                if ("\\`*_{}[]()#+-.!|~".Contains(c)) output.Append('\\');
                output.Append(c);
            }
        }
        return output.ToString();
    }
}
