package com.rehabeasy.service;

import com.rehabeasy.model.PatientClinicalNote;
import com.rehabeasy.model.PatientClinicalNoteHistoryEntry;
import com.rehabeasy.model.PatientHistorySnapshot;
import com.rehabeasy.model.PatientTestHistoryEntry;
import com.rehabeasy.model.RehabEasyRecord;

import java.util.Comparator;
import java.util.List;

public final class DefaultPatientHistoryService implements PatientHistoryService {
    private final RecordStore recordStore;
    private final ClinicalNoteStore clinicalNoteStore;

    public DefaultPatientHistoryService(RecordStore recordStore, ClinicalNoteStore clinicalNoteStore) {
        this.recordStore = recordStore;
        this.clinicalNoteStore = clinicalNoteStore;
    }

    @Override
    public PatientHistorySnapshot getPatientHistory(String patientId) {
        String normalizedPatientId = patientId == null ? "" : patientId.trim();
        if (normalizedPatientId.isBlank()) {
            throw new IllegalArgumentException("Informe o ID do paciente para consultar o historico.");
        }

        List<RehabEasyRecord> records = recordStore.getRecordsByPatientId(normalizedPatientId);
        List<PatientClinicalNoteHistoryEntry> noteHistory =
                clinicalNoteStore.getClinicalNoteHistory(normalizedPatientId);
        PatientClinicalNote currentNote = clinicalNoteStore.getClinicalNote(normalizedPatientId);

        List<PatientTestHistoryEntry> tests = records.stream()
                .map(this::mapTestHistoryEntry)
                .sorted(Comparator.comparing(PatientTestHistoryEntry::receivedAt).reversed())
                .toList();
        String patientName = records.stream()
                .map(record -> PatientRecordHelper.tryGetPatientName(record.rawPayloadJson()))
                .filter(name -> name != null && !name.isBlank())
                .findFirst()
                .orElse(null);

        return new PatientHistorySnapshot(
                normalizedPatientId,
                patientName,
                tests,
                noteHistory,
                currentNote);
    }

    @Override
    public String buildHistoryReport(PatientHistorySnapshot history) {
        return PatientHistoryReportBuilder.build(history);
    }

    private PatientTestHistoryEntry mapTestHistoryEntry(RehabEasyRecord record) {
        String testType = PatientRecordHelper.resolveTestType(record);
        return new PatientTestHistoryEntry(
                record.id(),
                testType,
                record.title(),
                record.receivedAt(),
                PatientTestMetricsExtractor.buildMetricsSummary(record.rawPayloadJson(), testType),
                PatientTestMetricsExtractor.buildDetailText(record));
    }
}
