namespace RehabEasy.Domain.Models;

public sealed class PatientHistorySnapshot
{
    public string PatientId { get; init; } = string.Empty;
    public string? PatientName { get; init; }
    public IReadOnlyList<PatientTestHistoryEntry> Tests { get; init; } = Array.Empty<PatientTestHistoryEntry>();
    public IReadOnlyList<PatientClinicalNoteHistoryEntry> ClinicalNotes { get; init; } =
        Array.Empty<PatientClinicalNoteHistoryEntry>();
    public PatientClinicalNote? CurrentClinicalNote { get; init; }
}
