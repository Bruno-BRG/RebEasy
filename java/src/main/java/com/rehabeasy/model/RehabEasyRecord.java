package com.rehabeasy.model;

import java.time.Instant;
import java.util.List;

public record RehabEasyRecord(
        String id,
        String sourceId,
        String title,
        String sender,
        String recipient,
        Instant receivedAt,
        String summary,
        String plainTextContent,
        String htmlContent,
        List<String> tags,
        String rawPayloadJson,
        String patientId,
        String testType,
        String pdfLocalPath
) {
    public RehabEasyRecord {
        id = safe(id);
        sourceId = safe(sourceId);
        title = safe(title);
        sender = safe(sender);
        recipient = safe(recipient);
        receivedAt = receivedAt == null ? Instant.EPOCH : receivedAt;
        summary = safe(summary);
        plainTextContent = safe(plainTextContent);
        htmlContent = safe(htmlContent);
        tags = tags == null ? List.of() : List.copyOf(tags);
        rawPayloadJson = rawPayloadJson == null || rawPayloadJson.isBlank() ? "{}" : rawPayloadJson;
        patientId = safe(patientId);
        testType = safe(testType);
        pdfLocalPath = safe(pdfLocalPath);
    }

    private static String safe(String value) {
        return value == null ? "" : value;
    }
}
