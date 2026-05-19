using System.Globalization;
using System.Text.Json;
using RehabEasy.Domain.Models;

namespace RehabEasy.Infrastructure.Services;

internal static class PayloadRecordMapper
{
    private static readonly string[] RecordArrayKeys = ["records", "registros", "items", "itens", "data", "dados"];
    private static readonly string[] IdKeys = ["id", "record_id", "registro_id", "external_id", "source_id"];
    private static readonly string[] TitleKeys = ["title", "titulo", "subject", "assunto", "name", "nome", "evento", "tipo"];
    private static readonly string[] SenderKeys = ["sender", "source", "source_system", "from", "remetente", "origem"];
    private static readonly string[] RecipientKeys = ["recipient", "destination", "to", "destinatario", "destino"];
    private static readonly string[] DateKeys = ["received_at", "created_at", "updated_at", "data", "date", "timestamp"];
    private static readonly string[] SummaryKeys = ["summary", "resumo", "snippet", "descricao", "description"];
    private static readonly string[] ContentKeys = ["content", "body", "plain_text", "plainTextBody", "mensagem", "observacoes", "notes"];
    private static readonly string[] HtmlKeys = ["html", "html_body", "htmlBody"];
    private static readonly string[] TagsKeys = ["tags", "labels", "etiquetas", "categorias"];

    public static IReadOnlyList<RehabEasyRecord> Map(string payloadId, JsonElement payload, DateTimeOffset importedAt)
    {
        List<JsonElement> sourceRecords = ExtractRecordElements(payload);
        if (sourceRecords.Count == 0 && payload.ValueKind == JsonValueKind.Object)
        {
            sourceRecords.Add(payload);
        }

        return sourceRecords
            .Where(record => record.ValueKind == JsonValueKind.Object)
            .Select((record, index) => MapRecord(payloadId, record, index, importedAt))
            .OrderByDescending(record => record.ReceivedAt)
            .ToList();
    }

    public static string GetSourceName(JsonElement payload)
    {
        return GetString(payload, "source", "source_system", "sistema", "origem") ?? "api";
    }

    private static RehabEasyRecord MapRecord(string payloadId, JsonElement record, int index, DateTimeOffset importedAt)
    {
        string sourceId = GetString(record, IdKeys) ?? $"{payloadId}:{index + 1}";
        string title = GetString(record, TitleKeys) ?? $"Registro {index + 1}";
        string summary = GetString(record, SummaryKeys) ?? string.Empty;
        string rawJson = record.GetRawText();

        return new RehabEasyRecord
        {
            Id = CreateStableRecordId(payloadId, sourceId, index),
            SourceId = sourceId,
            Title = title,
            Sender = GetString(record, SenderKeys) ?? "api",
            Recipient = GetString(record, RecipientKeys) ?? "RehabEasy",
            ReceivedAt = GetDate(record, DateKeys) ?? importedAt,
            Summary = summary,
            PlainTextContent = GetString(record, ContentKeys) ?? summary,
            HtmlContent = GetString(record, HtmlKeys) ?? string.Empty,
            Tags = GetTags(record),
            RawPayloadJson = rawJson
        };
    }

    private static List<JsonElement> ExtractRecordElements(JsonElement payload)
    {
        List<JsonElement> records = [];

        if (payload.ValueKind == JsonValueKind.Array)
        {
            records.AddRange(payload.EnumerateArray());
            return records;
        }

        if (payload.ValueKind != JsonValueKind.Object)
        {
            return records;
        }

        foreach (string key in RecordArrayKeys)
        {
            if (TryGetProperty(payload, key, out JsonElement candidate) &&
                candidate.ValueKind == JsonValueKind.Array)
            {
                records.AddRange(candidate.EnumerateArray());
                return records;
            }
        }

        return records;
    }

    private static IReadOnlyList<string> GetTags(JsonElement record)
    {
        foreach (string key in TagsKeys)
        {
            if (!TryGetProperty(record, key, out JsonElement value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray()
                    .Select(TagToString)
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Select(tag => tag!)
                    .ToList();
            }

            string? scalar = ValueToString(value);
            if (!string.IsNullOrWhiteSpace(scalar))
            {
                return scalar.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }

        return Array.Empty<string>();
    }

    private static string? GetString(JsonElement record, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (TryGetProperty(record, key, out JsonElement value))
            {
                string? text = ValueToString(value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static DateTimeOffset? GetDate(JsonElement record, params string[] keys)
    {
        string? rawDate = GetString(record, keys);
        if (string.IsNullOrWhiteSpace(rawDate))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
        {
            return parsed;
        }

        return long.TryParse(rawDate, out long unixSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
            : null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ValueToString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static string? TagToString(JsonElement value)
    {
        return ValueToString(value);
    }

    private static string CreateStableRecordId(string payloadId, string sourceId, int index)
    {
        return $"{payloadId}:{sourceId}:{index + 1}";
    }
}
