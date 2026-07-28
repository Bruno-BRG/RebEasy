using System.Text.Json;
using Microsoft.Data.Sqlite;
using RehabEasy.Domain.Contracts;
using RehabEasy.Domain.Models;

namespace RehabEasy.Infrastructure.Services;

public sealed class SqliteRecordStore : IRecordStore, IClinicalNoteStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

    public SqliteRecordStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);

        await ExecuteNonQueryAsync(connection, cancellationToken, """
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

            CREATE INDEX IF NOT EXISTS idx_records_received_at ON records(received_at DESC);

            CREATE TABLE IF NOT EXISTS patient_clinical_notes (
                patient_id TEXT PRIMARY KEY,
                content TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS patient_clinical_note_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                patient_id TEXT NOT NULL,
                content TEXT NOT NULL,
                saved_at TEXT NOT NULL
            );
            """);

        await EnsureColumnAsync(connection, cancellationToken, "records", "patient_id", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, cancellationToken, "records", "test_type", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, cancellationToken, "records", "pdf_local_path", "TEXT NOT NULL DEFAULT ''");

        await ExecuteNonQueryAsync(connection, cancellationToken, """
            CREATE INDEX IF NOT EXISTS idx_records_patient_id ON records(patient_id, received_at DESC);

            CREATE INDEX IF NOT EXISTS idx_patient_clinical_note_history
                ON patient_clinical_note_history(patient_id, saved_at DESC);
            """);

        await BackfillRecordMetadataAsync(connection, cancellationToken);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken,
        string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveRecordsAsync(IEnumerable<RehabEasyRecord> records, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction();

        foreach (RehabEasyRecord record in records)
        {
            RehabEasyRecord normalizedRecord = NormalizeRecordMetadata(record);

            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO records (
                    id, source_id, title, sender, recipient, received_at, summary,
                    plain_text_content, html_content, tags_json, raw_payload_json, imported_at,
                    patient_id, test_type, pdf_local_path
                )
                VALUES (
                    $id, $source_id, $title, $sender, $recipient, $received_at, $summary,
                    $plain_text_content, $html_content, $tags_json, $raw_payload_json, $imported_at,
                    $patient_id, $test_type, $pdf_local_path
                )
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
                    pdf_local_path = excluded.pdf_local_path;
                """;

            command.Parameters.AddWithValue("$id", normalizedRecord.Id);
            command.Parameters.AddWithValue("$source_id", normalizedRecord.SourceId);
            command.Parameters.AddWithValue("$title", normalizedRecord.Title);
            command.Parameters.AddWithValue("$sender", normalizedRecord.Sender);
            command.Parameters.AddWithValue("$recipient", normalizedRecord.Recipient);
            command.Parameters.AddWithValue("$received_at", normalizedRecord.ReceivedAt.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$summary", normalizedRecord.Summary);
            command.Parameters.AddWithValue("$plain_text_content", normalizedRecord.PlainTextContent);
            command.Parameters.AddWithValue("$html_content", normalizedRecord.HtmlContent);
            command.Parameters.AddWithValue("$tags_json", JsonSerializer.Serialize(normalizedRecord.Tags, JsonOptions));
            command.Parameters.AddWithValue("$raw_payload_json", normalizedRecord.RawPayloadJson);
            command.Parameters.AddWithValue("$imported_at", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$patient_id", normalizedRecord.PatientId);
            command.Parameters.AddWithValue("$test_type", normalizedRecord.TestType);
            command.Parameters.AddWithValue("$pdf_local_path", normalizedRecord.PdfLocalPath);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RehabEasyRecord>> SearchAsync(string? query, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();

        if (string.IsNullOrWhiteSpace(query))
        {
            command.CommandText = """
                SELECT id, source_id, title, sender, recipient, received_at, summary,
                       plain_text_content, html_content, tags_json, raw_payload_json,
                       patient_id, test_type, pdf_local_path
                FROM records
                ORDER BY received_at DESC
                LIMIT 500;
                """;
        }
        else
        {
            command.CommandText = """
                SELECT id, source_id, title, sender, recipient, received_at, summary,
                       plain_text_content, html_content, tags_json, raw_payload_json,
                       patient_id, test_type, pdf_local_path
                FROM records
                WHERE title LIKE $query
                   OR sender LIKE $query
                   OR recipient LIKE $query
                   OR summary LIKE $query
                   OR plain_text_content LIKE $query
                   OR raw_payload_json LIKE $query
                   OR patient_id LIKE $query
                   OR test_type LIKE $query
                ORDER BY received_at DESC
                LIMIT 500;
                """;
            command.Parameters.AddWithValue("$query", $"%{query.Trim()}%");
        }

        return await ReadRecordsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<RehabEasyRecord>> GetRecordsByPatientIdAsync(
        string patientId,
        CancellationToken cancellationToken)
    {
        string normalizedPatientId = NormalizePatientId(patientId);
        if (string.IsNullOrWhiteSpace(normalizedPatientId))
        {
            return Array.Empty<RehabEasyRecord>();
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, source_id, title, sender, recipient, received_at, summary,
                   plain_text_content, html_content, tags_json, raw_payload_json,
                   patient_id, test_type, pdf_local_path
            FROM records
            WHERE patient_id = $patient_id
               OR raw_payload_json LIKE $payload_pattern
            ORDER BY received_at DESC
            LIMIT 500;
            """;
        command.Parameters.AddWithValue("$patient_id", normalizedPatientId);
        command.Parameters.AddWithValue("$payload_pattern", $"%\"external_id\":\"{normalizedPatientId}\"%");

        IReadOnlyList<RehabEasyRecord> records = await ReadRecordsAsync(command, cancellationToken);
        return records
            .Where(record => string.Equals(
                PatientRecordHelper.ResolvePatientId(record),
                normalizedPatientId,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task DeleteRecordAsync(string id, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM records WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PatientClinicalNote?> GetClinicalNoteAsync(string patientId, CancellationToken cancellationToken)
    {
        string normalizedPatientId = NormalizePatientId(patientId);
        if (string.IsNullOrWhiteSpace(normalizedPatientId))
        {
            return null;
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT patient_id, content, updated_at
            FROM patient_clinical_notes
            WHERE patient_id = $patient_id;
            """;
        command.Parameters.AddWithValue("$patient_id", normalizedPatientId);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PatientClinicalNote
        {
            PatientId = reader.GetString(0),
            Content = reader.GetString(1),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(2))
        };
    }

    public async Task SaveClinicalNoteAsync(string patientId, string content, CancellationToken cancellationToken)
    {
        string normalizedPatientId = NormalizePatientId(patientId);
        if (string.IsNullOrWhiteSpace(normalizedPatientId))
        {
            throw new InvalidOperationException("Informe o ID do paciente para salvar o prontuario.");
        }

        DateTimeOffset updatedAt = DateTimeOffset.UtcNow;
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction();

        await using (SqliteCommand historyCommand = connection.CreateCommand())
        {
            historyCommand.Transaction = transaction;
            historyCommand.CommandText = """
                INSERT INTO patient_clinical_note_history (patient_id, content, saved_at)
                VALUES ($patient_id, $content, $saved_at);
                """;
            historyCommand.Parameters.AddWithValue("$patient_id", normalizedPatientId);
            historyCommand.Parameters.AddWithValue("$content", content);
            historyCommand.Parameters.AddWithValue("$saved_at", updatedAt.UtcDateTime.ToString("O"));
            await historyCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO patient_clinical_notes (patient_id, content, updated_at)
                VALUES ($patient_id, $content, $updated_at)
                ON CONFLICT(patient_id) DO UPDATE SET
                    content = excluded.content,
                    updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$patient_id", normalizedPatientId);
            command.Parameters.AddWithValue("$content", content);
            command.Parameters.AddWithValue("$updated_at", updatedAt.UtcDateTime.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PatientClinicalNoteHistoryEntry>> GetClinicalNoteHistoryAsync(
        string patientId,
        CancellationToken cancellationToken)
    {
        string normalizedPatientId = NormalizePatientId(patientId);
        if (string.IsNullOrWhiteSpace(normalizedPatientId))
        {
            return Array.Empty<PatientClinicalNoteHistoryEntry>();
        }

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, patient_id, content, saved_at
            FROM patient_clinical_note_history
            WHERE patient_id = $patient_id
            ORDER BY saved_at DESC
            LIMIT 200;
            """;
        command.Parameters.AddWithValue("$patient_id", normalizedPatientId);

        List<PatientClinicalNoteHistoryEntry> entries = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new PatientClinicalNoteHistoryEntry
            {
                Id = reader.GetInt64(0),
                PatientId = reader.GetString(1),
                Content = reader.GetString(2),
                SavedAt = DateTimeOffset.Parse(reader.GetString(3))
            });
        }

        return entries;
    }

    private static RehabEasyRecord NormalizeRecordMetadata(RehabEasyRecord record)
    {
        return new RehabEasyRecord
        {
            Id = record.Id,
            SourceId = record.SourceId,
            Title = record.Title,
            Sender = record.Sender,
            Recipient = record.Recipient,
            ReceivedAt = record.ReceivedAt,
            Summary = record.Summary,
            PlainTextContent = record.PlainTextContent,
            HtmlContent = record.HtmlContent,
            Tags = record.Tags,
            RawPayloadJson = record.RawPayloadJson,
            PatientId = PatientRecordHelper.ResolvePatientId(record),
            TestType = PatientRecordHelper.ResolveTestType(record),
            PdfLocalPath = record.PdfLocalPath
        };
    }

    private static string NormalizePatientId(string patientId)
    {
        return patientId.Trim();
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken,
        string tableName,
        string columnName,
        string columnDefinition)
    {
        await using SqliteCommand checkCommand = connection.CreateCommand();
        checkCommand.CommandText = $"PRAGMA table_info({tableName});";

        await using SqliteDataReader reader = await checkCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await reader.DisposeAsync();

        await using SqliteCommand alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task BackfillRecordMetadataAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand selectCommand = connection.CreateCommand();
        selectCommand.CommandText = """
            SELECT id, raw_payload_json, patient_id, test_type
            FROM records
            WHERE patient_id = '' OR test_type = '';
            """;

        List<(string Id, string PatientId, string TestType)> updates = [];
        await using (SqliteDataReader reader = await selectCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                string id = reader.GetString(0);
                string rawPayloadJson = reader.GetString(1);
                string patientId = reader.GetString(2);
                string testType = reader.GetString(3);

                updates.Add((
                    id,
                    string.IsNullOrWhiteSpace(patientId)
                        ? PatientRecordHelper.TryGetPatientExternalId(rawPayloadJson) ?? string.Empty
                        : patientId,
                    string.IsNullOrWhiteSpace(testType)
                        ? PatientRecordHelper.ResolveTestType(rawPayloadJson)
                        : testType));
            }
        }

        foreach ((string id, string patientId, string testType) in updates)
        {
            await using SqliteCommand updateCommand = connection.CreateCommand();
            updateCommand.CommandText = """
                UPDATE records
                SET patient_id = $patient_id, test_type = $test_type
                WHERE id = $id;
                """;
            updateCommand.Parameters.AddWithValue("$id", id);
            updateCommand.Parameters.AddWithValue("$patient_id", patientId);
            updateCommand.Parameters.AddWithValue("$test_type", testType);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<RehabEasyRecord>> ReadRecordsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        List<RehabEasyRecord> records = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(MapRecord(reader));
        }

        return records;
    }

    private static RehabEasyRecord MapRecord(SqliteDataReader reader)
    {
        string tagsJson = reader.GetString(9);
        IReadOnlyList<string>? tags = JsonSerializer.Deserialize<IReadOnlyList<string>>(tagsJson, JsonOptions);

        return new RehabEasyRecord
        {
            Id = reader.GetString(0),
            SourceId = reader.GetString(1),
            Title = reader.GetString(2),
            Sender = reader.GetString(3),
            Recipient = reader.GetString(4),
            ReceivedAt = DateTimeOffset.Parse(reader.GetString(5)),
            Summary = reader.GetString(6),
            PlainTextContent = reader.GetString(7),
            HtmlContent = reader.GetString(8),
            Tags = tags ?? Array.Empty<string>(),
            RawPayloadJson = reader.GetString(10),
            PatientId = reader.GetString(11),
            TestType = reader.GetString(12),
            PdfLocalPath = reader.FieldCount > 13 && !reader.IsDBNull(13)
                ? reader.GetString(13)
                : string.Empty
        };
    }
}
