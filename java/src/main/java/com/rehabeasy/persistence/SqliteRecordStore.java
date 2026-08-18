package com.rehabeasy.persistence;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.core.type.TypeReference;
import com.rehabeasy.json.JsonSupport;
import com.rehabeasy.model.PatientClinicalNote;
import com.rehabeasy.model.PatientClinicalNoteHistoryEntry;
import com.rehabeasy.model.RehabEasyRecord;
import com.rehabeasy.service.ClinicalNoteStore;
import com.rehabeasy.service.PatientRecordHelper;
import com.rehabeasy.service.RecordStore;

import java.nio.file.Files;
import java.nio.file.Path;
import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;

public final class SqliteRecordStore implements RecordStore, ClinicalNoteStore, AutoCloseable {
    private static final TypeReference<List<String>> TAG_LIST_TYPE = new TypeReference<>() {
    };

    private final Path databasePath;
    private final String connectionUrl;

    public SqliteRecordStore(Path databasePath) {
        this.databasePath = databasePath.toAbsolutePath().normalize();
        this.connectionUrl = "jdbc:sqlite:" + this.databasePath;
        try {
            Path parent = this.databasePath.getParent();
            if (parent != null) {
                Files.createDirectories(parent);
            }
        } catch (Exception exception) {
            throw failure("Nao foi possivel preparar a pasta do SQLite.", exception);
        }
    }

    public void initialize() {
        try (Connection connection = openConnection();
             Statement statement = connection.createStatement()) {
            statement.executeUpdate("""
                    CREATE TABLE IF NOT EXISTS records (
                        id TEXT PRIMARY KEY,
                        source_id TEXT NOT NULL,
                        title TEXT NOT NULL,
                        sender TEXT NOT NULL,
                        recipient TEXT NOT NULL,
                        received_at TEXT NOT NULL,
                        summary TEXT NOT NULL,
                        plain_text_content TEXT NOT NULL,
                        html_content TEXT NOT NULL,
                        tags_json TEXT NOT NULL,
                        raw_payload_json TEXT NOT NULL,
                        imported_at TEXT NOT NULL
                    );
                    """);
            statement.executeUpdate("""
                    CREATE INDEX IF NOT EXISTS idx_records_received_at
                    ON records(received_at DESC);
                    """);
            statement.executeUpdate("""
                    CREATE TABLE IF NOT EXISTS patient_clinical_notes (
                        patient_id TEXT PRIMARY KEY,
                        content TEXT NOT NULL,
                        updated_at TEXT NOT NULL
                    );
                    """);
            statement.executeUpdate("""
                    CREATE TABLE IF NOT EXISTS patient_clinical_note_history (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        patient_id TEXT NOT NULL,
                        content TEXT NOT NULL,
                        saved_at TEXT NOT NULL
                    );
                    """);

            ensureColumn(connection, "records", "patient_id", "TEXT NOT NULL DEFAULT ''");
            ensureColumn(connection, "records", "test_type", "TEXT NOT NULL DEFAULT ''");
            ensureColumn(connection, "records", "pdf_local_path", "TEXT NOT NULL DEFAULT ''");

            statement.executeUpdate("""
                    CREATE INDEX IF NOT EXISTS idx_records_patient_id
                    ON records(patient_id, received_at DESC);
                    """);
            statement.executeUpdate("""
                    CREATE INDEX IF NOT EXISTS idx_patient_clinical_note_history
                    ON patient_clinical_note_history(patient_id, saved_at DESC);
                    """);
        } catch (SQLException exception) {
            throw failure("Nao foi possivel inicializar o banco local.", exception);
        }
        backfillRecordMetadata();
    }

    @Override
    public void saveRecords(List<RehabEasyRecord> records) {
        if (records == null || records.isEmpty()) {
            return;
        }
        try (Connection connection = openConnection();
             PreparedStatement command = connection.prepareStatement("""
                     INSERT INTO records (
                         id, source_id, title, sender, recipient, received_at, summary,
                         plain_text_content, html_content, tags_json, raw_payload_json, imported_at,
                         patient_id, test_type, pdf_local_path
                     )
                     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                     ON CONFLICT(id) DO UPDATE SET
                         source_id = excluded.source_id,
                         title = excluded.title,
                         sender = excluded.sender,
                         recipient = excluded.recipient,
                         received_at = excluded.received_at,
                         summary = excluded.summary,
                         plain_text_content = excluded.plain_text_content,
                         html_content = excluded.html_content,
                         tags_json = excluded.tags_json,
                         raw_payload_json = excluded.raw_payload_json,
                         imported_at = excluded.imported_at,
                         patient_id = excluded.patient_id,
                         test_type = excluded.test_type,
                         pdf_local_path = excluded.pdf_local_path
                     """)) {
            connection.setAutoCommit(false);
            for (RehabEasyRecord record : records) {
                RehabEasyRecord normalized = normalizeRecordMetadata(record);
                command.setString(1, normalized.id());
                command.setString(2, normalized.sourceId());
                command.setString(3, normalized.title());
                command.setString(4, normalized.sender());
                command.setString(5, normalized.recipient());
                command.setString(6, normalized.receivedAt().toString());
                command.setString(7, normalized.summary());
                command.setString(8, normalized.plainTextContent());
                command.setString(9, normalized.htmlContent());
                command.setString(10, serializeTags(normalized.tags()));
                command.setString(11, normalized.rawPayloadJson());
                command.setString(12, Instant.now().toString());
                command.setString(13, normalized.patientId());
                command.setString(14, normalized.testType());
                command.setString(15, normalized.pdfLocalPath());
                command.addBatch();
            }
            command.executeBatch();
            connection.commit();
        } catch (SQLException exception) {
            throw failure("Nao foi possivel salvar os registros locais.", exception);
        }
    }

    @Override
    public List<RehabEasyRecord> search(String query) {
        String normalizedQuery = query == null ? "" : query.trim();
        String sql = normalizedQuery.isBlank()
                ? """
                  SELECT id, source_id, title, sender, recipient, received_at, summary,
                         plain_text_content, html_content, tags_json, raw_payload_json,
                         patient_id, test_type, pdf_local_path
                  FROM records
                  ORDER BY received_at DESC
                  LIMIT 500
                  """
                : """
                  SELECT id, source_id, title, sender, recipient, received_at, summary,
                         plain_text_content, html_content, tags_json, raw_payload_json,
                         patient_id, test_type, pdf_local_path
                  FROM records
                  WHERE title LIKE ?
                     OR sender LIKE ?
                     OR recipient LIKE ?
                     OR summary LIKE ?
                     OR plain_text_content LIKE ?
                     OR raw_payload_json LIKE ?
                     OR patient_id LIKE ?
                     OR test_type LIKE ?
                  ORDER BY received_at DESC
                  LIMIT 500
                  """;

        try (Connection connection = openConnection();
             PreparedStatement command = connection.prepareStatement(sql)) {
            if (!normalizedQuery.isBlank()) {
                String pattern = "%" + normalizedQuery + "%";
                for (int index = 1; index <= 8; index++) {
                    command.setString(index, pattern);
                }
            }
            try (ResultSet resultSet = command.executeQuery()) {
                return readRecords(resultSet);
            }
        } catch (SQLException exception) {
            throw failure("Nao foi possivel buscar os registros locais.", exception);
        }
    }

    @Override
    public List<RehabEasyRecord> getRecordsByPatientId(String patientId) {
        String normalizedPatientId = normalizePatientId(patientId);
        if (normalizedPatientId.isBlank()) {
            return List.of();
        }

        try (Connection connection = openConnection();
             PreparedStatement command = connection.prepareStatement("""
                     SELECT id, source_id, title, sender, recipient, received_at, summary,
                            plain_text_content, html_content, tags_json, raw_payload_json,
                            patient_id, test_type, pdf_local_path
                     FROM records
                     WHERE patient_id = ?
                        OR raw_payload_json LIKE ?
                     ORDER BY received_at DESC
                     LIMIT 500
                     """)) {
            command.setString(1, normalizedPatientId);
            command.setString(2, "%\"external_id\":\"" + normalizedPatientId + "\"%");
            try (ResultSet resultSet = command.executeQuery()) {
                return readRecords(resultSet).stream()
                        .filter(record -> PatientRecordHelper.resolvePatientId(record)
                                .equalsIgnoreCase(normalizedPatientId))
                        .toList();
            }
        } catch (SQLException exception) {
            throw failure("Nao foi possivel carregar o historico do paciente.", exception);
        }
    }

    @Override
    public void deleteRecord(String id) {
        try (Connection connection = openConnection();
             PreparedStatement command = connection.prepareStatement("DELETE FROM records WHERE id = ?")) {
            command.setString(1, id);
            command.executeUpdate();
        } catch (SQLException exception) {
            throw failure("Nao foi possivel apagar o registro local.", exception);
        }
    }

    @Override
    public PatientClinicalNote getClinicalNote(String patientId) {
        String normalizedPatientId = normalizePatientId(patientId);
        if (normalizedPatientId.isBlank()) {
            return null;
        }
        try (Connection connection = openConnection();
             PreparedStatement command = connection.prepareStatement("""
                     SELECT patient_id, content, updated_at
                     FROM patient_clinical_notes
                     WHERE patient_id = ?
                     """)) {
            command.setString(1, normalizedPatientId);
            try (ResultSet resultSet = command.executeQuery()) {
                if (!resultSet.next()) {
                    return null;
                }
                return new PatientClinicalNote(
                        resultSet.getString(1),
                        resultSet.getString(2),
                        parseInstant(resultSet.getString(3)));
            }
        } catch (SQLException exception) {
            throw failure("Nao foi possivel carregar o prontuario.", exception);
        }
    }

    @Override
    public void saveClinicalNote(String patientId, String content) {
        String normalizedPatientId = normalizePatientId(patientId);
        if (normalizedPatientId.isBlank()) {
            throw new IllegalArgumentException("Informe o ID do paciente para salvar o prontuario.");
        }
        Instant updatedAt = Instant.now();
        try (Connection connection = openConnection();
             PreparedStatement historyCommand = connection.prepareStatement("""
                     INSERT INTO patient_clinical_note_history (patient_id, content, saved_at)
                     VALUES (?, ?, ?)
                     """);
             PreparedStatement command = connection.prepareStatement("""
                     INSERT INTO patient_clinical_notes (patient_id, content, updated_at)
                     VALUES (?, ?, ?)
                     ON CONFLICT(patient_id) DO UPDATE SET
                         content = excluded.content,
                         updated_at = excluded.updated_at
                     """)) {
            connection.setAutoCommit(false);
            historyCommand.setString(1, normalizedPatientId);
            historyCommand.setString(2, content);
            historyCommand.setString(3, updatedAt.toString());
            historyCommand.executeUpdate();

            command.setString(1, normalizedPatientId);
            command.setString(2, content);
            command.setString(3, updatedAt.toString());
            command.executeUpdate();
            connection.commit();
        } catch (SQLException exception) {
            throw failure("Nao foi possivel salvar o prontuario.", exception);
        }
    }

    @Override
    public List<PatientClinicalNoteHistoryEntry> getClinicalNoteHistory(String patientId) {
        String normalizedPatientId = normalizePatientId(patientId);
        if (normalizedPatientId.isBlank()) {
            return List.of();
        }
        try (Connection connection = openConnection();
             PreparedStatement command = connection.prepareStatement("""
                     SELECT id, patient_id, content, saved_at
                     FROM patient_clinical_note_history
                     WHERE patient_id = ?
                     ORDER BY saved_at DESC
                     LIMIT 200
                     """)) {
            command.setString(1, normalizedPatientId);
            try (ResultSet resultSet = command.executeQuery()) {
                List<PatientClinicalNoteHistoryEntry> entries = new ArrayList<>();
                while (resultSet.next()) {
                    entries.add(new PatientClinicalNoteHistoryEntry(
                            resultSet.getLong(1),
                            resultSet.getString(2),
                            resultSet.getString(3),
                            parseInstant(resultSet.getString(4))));
                }
                return entries;
            }
        } catch (SQLException exception) {
            throw failure("Nao foi possivel carregar o historico do prontuario.", exception);
        }
    }

    private Connection openConnection() throws SQLException {
        Connection connection = DriverManager.getConnection(connectionUrl);
        try (Statement statement = connection.createStatement()) {
            statement.execute("PRAGMA busy_timeout = 5000");
            statement.execute("PRAGMA foreign_keys = ON");
        }
        return connection;
    }

    private void ensureColumn(Connection connection, String table, String column, String definition)
            throws SQLException {
        try (PreparedStatement command = connection.prepareStatement("PRAGMA table_info(" + table + ")");
             ResultSet resultSet = command.executeQuery()) {
            while (resultSet.next()) {
                if (column.equalsIgnoreCase(resultSet.getString("name"))) {
                    return;
                }
            }
        }
        try (Statement statement = connection.createStatement()) {
            statement.executeUpdate("ALTER TABLE " + table + " ADD COLUMN " + column + " " + definition);
        }
    }

    private void backfillRecordMetadata() {
        try (Connection connection = openConnection();
             PreparedStatement select = connection.prepareStatement("""
                     SELECT id, raw_payload_json, patient_id, test_type
                     FROM records
                     WHERE patient_id = '' OR test_type = ''
                     """);
             ResultSet resultSet = select.executeQuery();
             PreparedStatement update = connection.prepareStatement("""
                     UPDATE records
                     SET patient_id = ?, test_type = ?
                     WHERE id = ?
                     """)) {
            while (resultSet.next()) {
                String patientId = resultSet.getString(3);
                String testType = resultSet.getString(4);
                if (patientId == null || patientId.isBlank()) {
                    patientId = defaultString(
                            PatientRecordHelper.tryGetPatientExternalId(resultSet.getString(2)));
                }
                if (testType == null || testType.isBlank()) {
                    testType = PatientRecordHelper.resolveTestType(resultSet.getString(2));
                }
                update.setString(1, patientId);
                update.setString(2, testType);
                update.setString(3, resultSet.getString(1));
                update.addBatch();
            }
            update.executeBatch();
        } catch (SQLException exception) {
            throw failure("Nao foi possivel atualizar os metadados locais.", exception);
        }
    }

    private List<RehabEasyRecord> readRecords(ResultSet resultSet) throws SQLException {
        List<RehabEasyRecord> records = new ArrayList<>();
        while (resultSet.next()) {
            records.add(new RehabEasyRecord(
                    resultSet.getString(1),
                    resultSet.getString(2),
                    resultSet.getString(3),
                    resultSet.getString(4),
                    resultSet.getString(5),
                    parseInstant(resultSet.getString(6)),
                    resultSet.getString(7),
                    resultSet.getString(8),
                    resultSet.getString(9),
                    deserializeTags(resultSet.getString(10)),
                    resultSet.getString(11),
                    resultSet.getString(12),
                    resultSet.getString(13),
                    resultSet.getString(14)
            ));
        }
        return records;
    }

    private RehabEasyRecord normalizeRecordMetadata(RehabEasyRecord record) {
        return new RehabEasyRecord(
                record.id(),
                record.sourceId(),
                record.title(),
                record.sender(),
                record.recipient(),
                record.receivedAt(),
                record.summary(),
                record.plainTextContent(),
                record.htmlContent(),
                record.tags(),
                record.rawPayloadJson(),
                PatientRecordHelper.resolvePatientId(record),
                PatientRecordHelper.resolveTestType(record),
                record.pdfLocalPath());
    }

    private static String serializeTags(List<String> tags) {
        try {
            return JsonSupport.MAPPER.writeValueAsString(tags);
        } catch (JsonProcessingException exception) {
            throw failure("Nao foi possivel serializar as tags do registro.", exception);
        }
    }

    private static List<String> deserializeTags(String tagsJson) {
        if (tagsJson == null || tagsJson.isBlank()) {
            return List.of();
        }
        try {
            return JsonSupport.MAPPER.readValue(tagsJson, TAG_LIST_TYPE);
        } catch (JsonProcessingException exception) {
            return List.of();
        }
    }

    private static Instant parseInstant(String value) {
        if (value == null || value.isBlank()) {
            return Instant.EPOCH;
        }
        try {
            return Instant.parse(value);
        } catch (Exception ignored) {
            return Instant.EPOCH;
        }
    }

    private static String normalizePatientId(String patientId) {
        return patientId == null ? "" : patientId.trim();
    }

    private static String defaultString(String value) {
        return value == null ? "" : value;
    }

    private static IllegalStateException failure(String message, Exception cause) {
        return new IllegalStateException(message, cause);
    }

    @Override
    public void close() {
    }
}
