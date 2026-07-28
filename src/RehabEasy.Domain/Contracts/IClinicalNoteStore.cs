using RehabEasy.Domain.Models;

namespace RehabEasy.Domain.Contracts;

public interface IClinicalNoteStore
{
    Task<PatientClinicalNote?> GetClinicalNoteAsync(string patientId, CancellationToken cancellationToken);
    Task SaveClinicalNoteAsync(string patientId, string content, CancellationToken cancellationToken);
    Task<IReadOnlyList<PatientClinicalNoteHistoryEntry>> GetClinicalNoteHistoryAsync(
        string patientId,
        CancellationToken cancellationToken);
}
