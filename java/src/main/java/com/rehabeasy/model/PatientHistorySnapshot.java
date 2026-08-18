package com.rehabeasy.model;

import java.util.List;

public record PatientHistorySnapshot(
        String patientId,
        String patientName,
        List<PatientTestHistoryEntry> tests,
        List<PatientClinicalNoteHistoryEntry> clinicalNotes,
        PatientClinicalNote currentClinicalNote
) {
    public PatientHistorySnapshot {
        patientId = patientId == null ? "" : patientId;
        tests = tests == null ? List.of() : List.copyOf(tests);
        clinicalNotes = clinicalNotes == null ? List.of() : List.copyOf(clinicalNotes);
    }
}
