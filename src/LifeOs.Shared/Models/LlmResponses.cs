namespace LifeOs.Api.Models;

public sealed record CaptureMetadata(string? Activity, double? Energy, double? Satisfaction, string? Location);
public sealed record CaptureClassification(
    CaptureType Type, string Title, string Summary, string[] Tags, bool ActionRequired, CaptureMetadata Metadata);

public sealed record ReviewContent(
    string[] KeyObservations, string[] RecurringThemes, string[] EnergyGivers,
    string[] EnergyDrainers, string[] OpenTasks, string[] BestContentIdeas,
    string[] Patterns, string? SuggestedExperiment)
{
    public static ReviewContent Empty => new([], [], [], [], [], [], [], null);
}

public sealed record WeeklyReview(
    DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd,
    string[] KeyObservations, string[] RecurringThemes, string[] EnergyGivers,
    string[] EnergyDrainers, string[] OpenTasks, string[] BestContentIdeas,
    string[] Patterns, string? SuggestedExperiment)
{
    public static WeeklyReview From(DateTimeOffset start, DateTimeOffset end, ReviewContent c) =>
        new(start, end, c.KeyObservations, c.RecurringThemes, c.EnergyGivers, c.EnergyDrainers,
            c.OpenTasks, c.BestContentIdeas, c.Patterns, c.SuggestedExperiment);
}
