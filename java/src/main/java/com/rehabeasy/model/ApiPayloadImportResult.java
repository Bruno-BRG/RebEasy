package com.rehabeasy.model;

import java.time.Instant;
import java.util.List;

public record ApiPayloadImportResult(
        String payloadId,
        String sourceName,
        Instant importedAt,
        String pdfUrl,
        String pdfLocalPath,
        List<RehabEasyRecord> records
) {
    public ApiPayloadImportResult {
        payloadId = payloadId == null ? "" : payloadId;
        sourceName = sourceName == null ? "" : sourceName;
        importedAt = importedAt == null ? Instant.EPOCH : importedAt;
        records = records == null ? List.of() : List.copyOf(records);
    }
}
