package com.rehabeasy.service;

import com.fasterxml.jackson.databind.JsonNode;
import com.rehabeasy.json.JsonSupport;
import com.rehabeasy.model.RehabEasyRecord;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.io.InputStream;
import java.time.Instant;
import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

class PayloadRecordMapperTest {
    @Test
    void mapsGenericPayloadUsingStableRecordFields() {
        JsonNode payload = fixture("generic_payload.json");

        List<RehabEasyRecord> records =
                PayloadRecordMapper.map("payload_generic", payload, Instant.parse("2026-08-17T12:00:00Z"), "");

        assertEquals(1, records.size());
        RehabEasyRecord record = records.getFirst();
        assertEquals("payload_generic:relatorio-001:1", record.id());
        assertEquals("Relatorio de atendimento 001", record.title());
        assertEquals("Sistema Origem", record.sender());
        assertEquals("Outro", record.testType());
        assertTrue(record.plainTextContent().contains("Conteudo completo"));
        assertEquals(List.of("triagem", "api", "rehabeasy"), record.tags());
    }

    @Test
    void detectsAllSupportedClinicalReportTypes() {
        RehabEasyRecord cvTug = mapSingle("cvtug_payload.json", "payload_cvtug");
        RehabEasyRecord equilibrio = mapSingle("equilibrio_payload.json", "payload_equilibrio");
        RehabEasyRecord indexIndex = mapSingle("indexindex_payload.json", "payload_index");

        assertEquals(PatientRecordHelper.TEST_TYPE_CVTUG, cvTug.testType());
        assertEquals("20251121100833", cvTug.patientId());
        assertTrue(cvTug.plainTextContent().contains("Sinalizadores"));

        assertEquals(PatientRecordHelper.TEST_TYPE_EQUILIBRIO, equilibrio.testType());
        assertEquals("20260610141907", equilibrio.patientId());
        assertTrue(equilibrio.plainTextContent().contains("Quocientes de Romberg"));

        assertEquals(PatientRecordHelper.TEST_TYPE_INDEX_INDEX, indexIndex.testType());
        assertEquals("R97o807t870", indexIndex.patientId());
        assertTrue(indexIndex.plainTextContent().contains("Assimetria entre maos"));
    }

    @Test
    void supportsPayloadObjectWithoutRecordsArray() {
        JsonNode payload = JsonSupport.readTree("""
                {
                  "source": "legacy",
                  "id": "legacy-1",
                  "title": "Registro legado",
                  "created_at": "2026-08-17T10:00:00Z",
                  "content": "Conteudo legado"
                }
                """);

        List<RehabEasyRecord> records =
                PayloadRecordMapper.map("payload_legacy", payload, Instant.now(), "");

        assertFalse(records.isEmpty());
        assertEquals("Registro legado", records.getFirst().title());
    }

    private RehabEasyRecord mapSingle(String resource, String payloadId) {
        List<RehabEasyRecord> records =
                PayloadRecordMapper.map(payloadId, fixture(resource), Instant.now(), "");
        assertEquals(1, records.size());
        return records.getFirst();
    }

    private JsonNode fixture(String resource) {
        try (InputStream stream = getClass().getResourceAsStream("/fixtures/" + resource)) {
            if (stream == null) {
                throw new IllegalStateException("Fixture nao encontrada: " + resource);
            }
            return JsonSupport.MAPPER.readTree(stream);
        } catch (IOException exception) {
            throw new IllegalStateException("Falha ao ler fixture: " + resource, exception);
        }
    }
}
