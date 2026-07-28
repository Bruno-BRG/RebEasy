namespace RehabEasy.App;

public sealed class PatientTimelineItem
{
    public string Category { get; init; } = string.Empty;
    public string Headline { get; init; } = string.Empty;
    public string Subline { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; }
    public string? RecordId { get; init; }
}
