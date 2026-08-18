package com.rehabeasy.ui;

import com.fasterxml.jackson.databind.JsonNode;
import com.rehabeasy.json.JsonSupport;
import com.rehabeasy.model.RehabEasyRecord;
import com.rehabeasy.service.PayloadRecordMapper;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.io.InputStream;
import java.time.Instant;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

class ClinicalMetricsServiceTest {
    @Test
    void extractsCvTugSummaryAndAlert() {
        RehabEasyRecord record = fixtureRecord("cvtug_payload.json", "payload_cvtug");

        ClinicalMetricsService.ClinicalMetrics metrics = ClinicalMetricsService.analyze(record);

        assertEquals("CvTUG", metrics.testType());
        assertEquals("10.4s", metrics.primaryValue());
        assertEquals("ALERTA", metrics.risk());
        assertTrue(metrics.alert());
        assertEquals(4, metrics.bars().size());
    }

    @Test
    void extractsEquilibrioAndIndexIndexMetrics() {
        ClinicalMetricsService.ClinicalMetrics equilibrio =
                ClinicalMetricsService.analyze(fixtureRecord("equilibrio_payload.json", "payload_equilibrio"));
        ClinicalMetricsService.ClinicalMetrics index =
                ClinicalMetricsService.analyze(fixtureRecord("indexindex_payload.json", "payload_index"));

        assertEquals("273.1mm", equilibrio.primaryValue());
        assertTrue(equilibrio.alert());
        assertEquals("3.2mm", index.primaryValue());
        assertEquals("ALERTA", index.risk());
        assertTrue(index.alert());
    }

    private RehabEasyRecord fixtureRecord(String resource, String payloadId) {
        try (InputStream stream = getClass().getResourceAsStream("/fixtures/" + resource)) {
            if (stream == null) {
                throw new IllegalStateException("Fixture nao encontrada: " + resource);
            }
            JsonNode payload = JsonSupport.MAPPER.readTree(stream);
            return PayloadRecordMapper.map(payloadId, payload, Instant.now(), "").getFirst();
        } catch (IOException exception) {
            throw new IllegalStateException(exception);
        }
    }
}
