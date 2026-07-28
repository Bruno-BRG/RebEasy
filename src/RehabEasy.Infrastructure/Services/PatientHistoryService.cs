using RehabEasy.Domain.Contracts;
using RehabEasy.Domain.Models;

namespace RehabEasy.Infrastructure.Services;

public sealed class PatientHistoryService : IPatientHistoryService
{
    private readonly IRecordStore _recordStore;
    private readonly IClinicalNoteStore _clinicalNoteStore;

    public PatientHistoryService(IRecordStore recordStore, IClinicalNoteStore clinicalNoteStore)
    {
        _recordStore = recordStore;
        _clinicalNoteStore = clinicalNoteStore;
    }

    public async Task<PatientHistorySnapshot> GetPatientHistoryAsync(string patientId, CancellationToken cancellationToken)
    {
        string normalizedPatientId = patientId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedPatientId))
        {
            throw new InvalidOperationException("Informe o ID do paciente para consultar o historico.");
        }

        IReadOnlyList<RehabEasyRecord> records =
            await _recordStore.GetRecordsByPatientIdAsync(normalizedPatientId, cancellationToken);
        IReadOnlyList<PatientClinicalNoteHistoryEntry> noteHistory =
            await _clinicalNoteStore.GetClinicalNoteHistoryAsync(normalizedPatientId, cancellationToken);
        PatientClinicalNote? currentNote =
            await _clinicalNoteStore.GetClinicalNoteAsync(normalizedPatientId, cancellationToken);

        List<PatientTestHistoryEntry> tests = records
            .Select(MapTestHistoryEntry)
            .OrderByDescending(entry => entry.ReceivedAt)
            .ToList();

        string? patientName = records
            .Select(record => PatientRecordHelper.TryGetPatientName(record.RawPayloadJson))
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

        return new PatientHistorySnapshot
        {
            PatientId = normalizedPatientId,
            PatientName = patientName,
            Tests = tests,
            ClinicalNotes = noteHistory,
            CurrentClinicalNote = currentNote
        };
    }

    public string BuildHistoryReport(PatientHistorySnapshot history)
    {
        return PatientHistoryReportBuilder.Build(history);
    }

    private static PatientTestHistoryEntry MapTestHistoryEntry(RehabEasyRecord record)
    {
        string testType = PatientRecordHelper.ResolveTestType(record);
        return new PatientTestHistoryEntry
        {
            RecordId = record.Id,
            TestType = testType,
            Title = record.Title,
            ReceivedAt = record.ReceivedAt,
            MetricsSummary = PatientTestMetricsExtractor.BuildMetricsSummary(record.RawPayloadJson, testType),
            DetailText = PatientTestMetricsExtractor.BuildDetailText(record)
        };
    }
}
