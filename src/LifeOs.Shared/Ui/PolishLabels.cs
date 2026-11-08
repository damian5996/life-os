using System.Globalization;
using LifeOs.Api.Models;

namespace LifeOs.Api.Ui;

public static class PolishLabels
{
    public static readonly (CaptureType? Type, string Label)[] Filters =
    [
        (null, "Wszystkie"), (CaptureType.FIELD_NOTE, "Notatki terenowe"),
        (CaptureType.TASK, "Zadania"), (CaptureType.CONTENT_IDEA, "Content"),
        (CaptureType.IDEA, "Pomysły"), (CaptureType.OTHER, "Inne")
    ];
    public static string Type(CaptureType? type) => type switch
    {
        CaptureType.FIELD_NOTE => "Notatka terenowa", CaptureType.TASK => "Zadanie",
        CaptureType.CONTENT_IDEA => "Pomysł na content", CaptureType.IDEA => "Pomysł",
        CaptureType.OTHER => "Inne", _ => "Bez klasyfikacji"
    };
    public static string Source(CaptureSource source) => source switch
    { CaptureSource.VOICE => "Głos", CaptureSource.TEXT => "Tekst", _ => "Inne" };
    public static string State(ProcessingState state) => state switch
    { ProcessingState.COMPLETED => "Ukończono", ProcessingState.FAILED => "Błąd klasyfikacji", _ => "Oczekuje" };
    public static string Date(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value,
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw")).ToString("d MMM yyyy, HH:mm", CultureInfo.GetCultureInfo("pl-PL"));
}
