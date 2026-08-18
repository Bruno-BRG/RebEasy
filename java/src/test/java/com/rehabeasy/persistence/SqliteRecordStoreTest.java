package com.rehabeasy.persistence;

import com.rehabeasy.model.PatientClinicalNote;
import com.rehabeasy.model.PatientClinicalNoteHistoryEntry;
import com.rehabeasy.model.RehabEasyRecord;
import com.rehabeasy.service.PayloadRecordMapper;
import com.rehabeasy.json.JsonSupport;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.io.InputStream;
import java.nio.file.Path;
import java.time.Instant;
import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

class SqliteRecordStoreTest {
    @Test
    void preservesRecordsAndClinicalNotesInCompatibleSchema(@TempDir Path tempDirectory) {
        SqliteRecordStore store = new SqliteRecordStore(tempDirectory.resolve("rehabeasy.db"));
        store.initialize();

        List<RehabEasyRecord> records = List.of(
                fixtureRecord("cvtug_payload.json", "payload_cvtug"),
                fixtureRecord("equilibrio_payload.json", "payload_equilibrio"),
                fixtureRecord("indexindex_payload.json", "payload_index"));
        store.saveRecords(records);

        assertEquals(3, store.search(null).size());
        assertEquals(1, store.getRecordsByPatientId("20251121100833").size());
        assertEquals(1, store.search("Index-Index").size());

        store.saveClinicalNote("20251121100833", "Evolucao inicial.");
        store.saveClinicalNote("20251121100833", "Evolucao atualizada.");

        PatientClinicalNote note = store.getClinicalNote("20251121100833");
        List<PatientClinicalNoteHistoryEntry> history =
                store.getClinicalNoteHistory("20251121100833");

        assertNotNull(note);
        assertEquals("Evolucao atualizada.", note.content());
        assertEquals(2, history.size());
        assertTrue(history.getFirst().savedAt().compareTo(history.getLast().savedAt()) >= 0);
        store.close();
    }

    @Test
    void initializesAnEmptyDatabaseWithoutExternalMigration(@TempDir Path tempDirectory) {
        Path database = tempDirectory.resolve("nested").resolve("rehabeasy.db");
        SqliteRecordStore store = new SqliteRecordStore(database);
        store.initialize();

        assertTrue(store.search(null).isEmpty());
        store.close();
    }

    private RehabEasyRecord fixtureRecord(String resource, String payloadId) {
        try (InputStream stream = getClass().getResourceAsStream("/fixtures/" + resource)) {
            if (stream == null) {
                throw new IllegalStateException("Fixture nao encontrada: " + resource);
            }
            return PayloadRecordMapper.map(
                    payloadId,
                    JsonSupport.MAPPER.readTree(stream),
                    Instant.now(),
                    "").getFirst();
        } catch (IOException exception) {
            throw new IllegalStateException(exception);
        }
    }
}
