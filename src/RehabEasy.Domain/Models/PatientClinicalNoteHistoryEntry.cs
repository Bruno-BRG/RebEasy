namespace RehabEasy.Domain.Models;

public sealed class PatientClinicalNoteHistoryEntry
{
    public long Id { get; init; }
    public string PatientId { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public DateTimeOffset SavedAt { get; init; }
}
