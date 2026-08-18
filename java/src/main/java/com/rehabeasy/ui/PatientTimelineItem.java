package com.rehabeasy.ui;

import java.time.Instant;

public record PatientTimelineItem(
        String category,
        String headline,
        String subline,
        Instant occurredAt,
        String recordId
) {
}
