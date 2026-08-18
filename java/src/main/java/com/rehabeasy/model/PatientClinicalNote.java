package com.rehabeasy.model;

import java.time.Instant;

public record PatientClinicalNote(
        String patientId,
        String content,
        Instant updatedAt
) {
    public PatientClinicalNote {
        patientId = patientId == null ? "" : patientId;
        content = content == null ? "" : content;
        updatedAt = updatedAt == null ? Instant.EPOCH : updatedAt;
    }
}
