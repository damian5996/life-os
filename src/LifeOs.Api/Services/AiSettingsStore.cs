using System.Text.Json;
using LifeOs.Api.Data;
using LifeOs.Api.Models;
using LifeOs.Api.Prompts;
using Microsoft.EntityFrameworkCore;

namespace LifeOs.Api.Services;

public sealed class AiSettingsRow
{
    public int Id { get; set; } = 1;
    public Guid Revision { get; set; } = Guid.NewGuid();
    public int ActiveVersion { get; set; }
    public string DraftJson { get; set; } = "{}";
    public string ActiveJson { get; set; } = "{}";
}

public sealed class AiSettingsStore(LifeOsDbContext db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static AiSettings Defaults() => new()
    {
        CaptureInstructions = LifeOsPrompts.Classification[..LifeOsPrompts.Classification.IndexOf("Dokładnie jedna kategoria:", StringComparison.Ordinal)] +
            "\nOznacz actionRequired tylko przy faktycznym działaniu. Metadane wyodrębniaj wyłącznie z jawnych informacji. Nie zgaduj ocen liczbowych.",
        ReviewInstructions = LifeOsPrompts.WeeklyReview,
        Categories =
        [
            Category(1, "Notatki terenowe", "Obserwacje emocji, energii, doświadczeń, relacji lub aktywności.", CaptureType.FIELD_NOTE),
            Category(2, "Zadania", "Konkretna intencja lub obowiązek działania, nie luźna możliwość.", CaptureType.TASK),
            Category(3, "Content", "Pomysł na post, artykuł, nagranie lub newsletter.", CaptureType.CONTENT_IDEA),
            Category(4, "Pomysły", "Pomysł, hipoteza lub kierunek bez wyraźnego zobowiązania.", CaptureType.IDEA),
            Category(5, "Inne", "Pozostałe notatki, które nie pasują do innych kategorii.", CaptureType.OTHER)
        ]
    };
    private static NoteCategory Category(int id, string name, string instructions, CaptureType type) => new()
    { Id = Guid.Parse($"00000000-0000-0000-0000-{id:000000000000}"), Name = name, Instructions = instructions, LegacyType = type };

    public static string? Validate(AiSettings? settings)
    {
        if (settings is null || settings.Categories is null || settings.Categories.Count is < 1 or > 50 ||
            settings.Categories.Any(c => c is null)) return "Podaj od 1 do 50 kategorii.";
        if (!settings.Categories.Any(c => c.Enabled)) return "Co najmniej jedna kategoria musi być aktywna.";
        if (settings.Categories.Any(c => c.Id == Guid.Empty || !Enum.IsDefined(c.LegacyType) ||
                string.IsNullOrWhiteSpace(c.Name) || c.Name.Trim().Length > 80 ||
                string.IsNullOrWhiteSpace(c.Instructions) || c.Instructions.Length > 4000))
            return "Każda kategoria wymaga nazwy (do 80 znaków) i instrukcji (do 4000 znaków).";
        if (settings.Categories.Select(c => c.Id).Distinct().Count() != settings.Categories.Count ||
            settings.Categories.Select(c => c.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != settings.Categories.Count)
            return "Nazwy i identyfikatory kategorii nie mogą się powtarzać.";
        if (string.IsNullOrWhiteSpace(settings.CaptureInstructions) || settings.CaptureInstructions.Length > 16000 ||
            string.IsNullOrWhiteSpace(settings.ReviewInstructions) || settings.ReviewInstructions.Length > 16000)
            return "Obie instrukcje AI są wymagane i mogą mieć do 16000 znaków.";
        return null;
    }
    public async Task<SettingsDocument> ReadAsync(CancellationToken ct)
    {
        var row = await db.AiSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
        return row is null ? new(Guid.Empty, 0, Defaults(), Defaults()) : Document(row);
    }
    public async Task<SettingsDocument?> SaveAsync(Guid revision, AiSettings draft, bool activate, CancellationToken ct)
    {
        var row = await db.AiSettings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if ((row?.Revision ?? Guid.Empty) != revision) return null;
        if (row is null)
        {
            row = new() { ActiveJson = JsonSerializer.Serialize(Defaults(), Json) };
            db.AiSettings.Add(row);
        }
        row.DraftJson = JsonSerializer.Serialize(draft, Json);
        if (activate) { row.ActiveJson = row.DraftJson; row.ActiveVersion++; }
        row.Revision = Guid.NewGuid();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return null; }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return null; }
        return Document(row);
    }
    private static SettingsDocument Document(AiSettingsRow row) => new(row.Revision, row.ActiveVersion,
        JsonSerializer.Deserialize<AiSettings>(row.DraftJson, Json)!, JsonSerializer.Deserialize<AiSettings>(row.ActiveJson, Json)!);
}
