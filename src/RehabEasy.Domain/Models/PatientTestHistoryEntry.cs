namespace RehabEasy.Domain.Models;

public sealed class PatientTestHistoryEntry
{
    public string RecordId { get; init; } = string.Empty;
    public string TestType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; init; }
    public string MetricsSummary { get; init; } = string.Empty;
    public string DetailText { get; init; } = string.Empty;
}
