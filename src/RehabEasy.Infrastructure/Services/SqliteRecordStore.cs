using System.Text.Json;
using Microsoft.Data.Sqlite;
using RehabEasy.Domain.Contracts;
using RehabEasy.Domain.Models;

namespace RehabEasy.Infrastructure.Services;

public sealed class SqliteRecordStore : IRecordStore
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
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
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
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveRecordsAsync(IEnumerable<RehabEasyRecord> records, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction();

        foreach (RehabEasyRecord record in records)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO records (
                    id, source_id, title, sender, recipient, received_at, summary,
                    plain_text_content, html_content, tags_json, raw_payload_json, imported_at
                )
                VALUES (
                    $id, $source_id, $title, $sender, $recipient, $received_at, $summary,
                    $plain_text_content, $html_content, $tags_json, $raw_payload_json, $imported_at
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
                    imported_at = excluded.imported_at;
                """;

            command.Parameters.AddWithValue("$id", record.Id);
            command.Parameters.AddWithValue("$source_id", record.SourceId);
            command.Parameters.AddWithValue("$title", record.Title);
            command.Parameters.AddWithValue("$sender", record.Sender);
            command.Parameters.AddWithValue("$recipient", record.Recipient);
            command.Parameters.AddWithValue("$received_at", record.ReceivedAt.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$summary", record.Summary);
            command.Parameters.AddWithValue("$plain_text_content", record.PlainTextContent);
            command.Parameters.AddWithValue("$html_content", record.HtmlContent);
            command.Parameters.AddWithValue("$tags_json", JsonSerializer.Serialize(record.Tags, JsonOptions));
            command.Parameters.AddWithValue("$raw_payload_json", record.RawPayloadJson);
            command.Parameters.AddWithValue("$imported_at", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
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
                       plain_text_content, html_content, tags_json, raw_payload_json
                FROM records
                ORDER BY received_at DESC
                LIMIT 500;
                """;
        }
        else
        {
            command.CommandText = """
                SELECT id, source_id, title, sender, recipient, received_at, summary,
                       plain_text_content, html_content, tags_json, raw_payload_json
                FROM records
                WHERE title LIKE $query
                   OR sender LIKE $query
                   OR recipient LIKE $query
                   OR summary LIKE $query
                   OR plain_text_content LIKE $query
                   OR raw_payload_json LIKE $query
                ORDER BY received_at DESC
                LIMIT 500;
                """;
            command.Parameters.AddWithValue("$query", $"%{query.Trim()}%");
        }

        List<RehabEasyRecord> records = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(MapRecord(reader));
        }

        return records;
    }

    public async Task DeleteRecordAsync(string id, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM records WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
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
            RawPayloadJson = reader.GetString(10)
        };
    }
}
