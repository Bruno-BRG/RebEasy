package com.rehabeasy.model;

import java.time.Instant;

public record PatientClinicalNoteHistoryEntry(
        long id,
        String patientId,
        String content,
        Instant savedAt
) {
    public PatientClinicalNoteHistoryEntry {
        patientId = patientId == null ? "" : patientId;
        content = content == null ? "" : content;
        savedAt = savedAt == null ? Instant.EPOCH : savedAt;
    }
}
