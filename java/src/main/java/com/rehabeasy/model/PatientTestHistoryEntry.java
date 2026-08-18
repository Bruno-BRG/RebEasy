package com.rehabeasy.model;

import java.time.Instant;

public record PatientTestHistoryEntry(
        String recordId,
        String testType,
        String title,
        Instant receivedAt,
        String metricsSummary,
        String detailText
) {
    public PatientTestHistoryEntry {
        recordId = recordId == null ? "" : recordId;
        testType = testType == null ? "" : testType;
        title = title == null ? "" : title;
        receivedAt = receivedAt == null ? Instant.EPOCH : receivedAt;
        metricsSummary = metricsSummary == null ? "" : metricsSummary;
        detailText = detailText == null ? "" : detailText;
    }
}
