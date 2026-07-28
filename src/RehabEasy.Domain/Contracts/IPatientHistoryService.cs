using RehabEasy.Domain.Models;

namespace RehabEasy.Domain.Contracts;

public interface IPatientHistoryService
{
    Task<PatientHistorySnapshot> GetPatientHistoryAsync(string patientId, CancellationToken cancellationToken);
    string BuildHistoryReport(PatientHistorySnapshot history);
}
