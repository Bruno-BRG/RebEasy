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
        string plainTextContent = BuildPlainTextContent(record, summary, rawJson);

        return new RehabEasyRecord
        {
            Id = CreateStableRecordId(payloadId, sourceId, index),
            SourceId = sourceId,
            Title = title,
            Sender = GetString(record, SenderKeys) ?? "api",
            Recipient = GetString(record, RecipientKeys) ?? "RehabEasy",
            ReceivedAt = GetDate(record, DateKeys) ?? importedAt,
            Summary = summary,
            PlainTextContent = plainTextContent,
            HtmlContent = GetString(record, HtmlKeys) ?? string.Empty,
            Tags = GetTags(record),
            RawPayloadJson = rawJson
        };
    }

    private static string BuildPlainTextContent(JsonElement record, string summary, string rawJson)
    {
        if (IsCvTugRecord(record))
        {
            string cvTugContent = BuildCvTugPlainTextContent(record, summary);
            if (!string.IsNullOrWhiteSpace(cvTugContent))
            {
                return cvTugContent;
            }
        }

        return GetString(record, ContentKeys) ?? summary;
    }

    private static bool IsCvTugRecord(JsonElement record)
    {
        string? sender = GetString(record, SenderKeys);
        if (string.Equals(sender, "CvTUG", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TryGetProperty(record, "assessment", out _)
            && TryGetProperty(record, "patient", out _);
    }

    private static string BuildCvTugPlainTextContent(JsonElement record, string summary)
    {
        List<string> sections = [];

        if (!string.IsNullOrWhiteSpace(summary))
        {
            sections.Add(summary.Trim());
        }

        string? narrative = GetString(record, ContentKeys);
        if (!string.IsNullOrWhiteSpace(narrative))
        {
            sections.Add(narrative.Trim());
        }

        if (TryGetProperty(record, "patient", out JsonElement patient))
        {
            List<string> patientParts = [];
            AddLabeledValue(patientParts, "Paciente", GetString(patient, "name"));
            AddLabeledValue(patientParts, "Idade", TryGetIntString(patient, "age_years"));
            AddLabeledValue(patientParts, "Sexo", GetString(patient, "sex"));
            AddLabeledValue(patientParts, "ID Externo", GetString(patient, "external_id"));

            if (patientParts.Count > 0)
            {
                sections.Add("Paciente:\n" + string.Join('\n', patientParts));
            }
        }

        if (TryGetProperty(record, "assessment", out JsonElement assessment))
        {
            string? performedAt = GetString(assessment, "performed_at");
            List<string> metricsLines = [];

            if (!string.IsNullOrWhiteSpace(performedAt))
            {
                metricsLines.Add($"Data do exame: {performedAt}");
            }

            if (TryGetProperty(assessment, "conditions", out JsonElement conditions) &&
                conditions.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement condition in conditions.EnumerateArray())
                {
                    string label = GetString(condition, "label") ?? GetString(condition, "code") ?? "Condicao";
                    string total = TryGetDoubleString(condition, "total_seconds") ?? "--";
                    string? dtc = TryGetDoubleString(condition, "dual_task_cost_percent");

                    List<string> phaseParts = [];
                    if (TryGetProperty(condition, "phases", out JsonElement phases))
                    {
                        AddPhaseValue(phaseParts, "Levantar", TryGetDoubleString(phases, "stand_seconds"));
                        AddPhaseValue(phaseParts, "Marcha", TryGetDoubleString(phases, "walk_seconds"));
                        AddPhaseValue(phaseParts, "Sentar", TryGetDoubleString(phases, "sit_seconds"));
                    }

                    string line = $"- {label}: total {total}s";
                    if (!string.IsNullOrWhiteSpace(dtc))
                    {
                        line += $"; DTC {dtc}%";
                    }

                    if (phaseParts.Count > 0)
                    {
                        line += $"; {string.Join("; ", phaseParts)}";
                    }

                    metricsLines.Add(line);
                }
            }

            if (metricsLines.Count > 0)
            {
                sections.Add("Resultados:\n" + string.Join('\n', metricsLines));
            }

            List<string> flagLines = BuildCvTugFlagLines(assessment);
            if (flagLines.Count > 0)
            {
                sections.Add("Sinalizadores:\n" + string.Join('\n', flagLines));
            }

            if (TryGetProperty(assessment, "methodology_notes", out JsonElement notes) &&
                notes.ValueKind == JsonValueKind.Array)
            {
                List<string> noteLines = notes.EnumerateArray()
                    .Select(ValueToString)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Select(text => $"- {text!}")
                    .ToList();

                if (noteLines.Count > 0)
                {
                    sections.Add("Notas metodologicas:\n" + string.Join('\n', noteLines));
                }
            }
        }

        return string.Join("\n\n", sections.Where(section => !string.IsNullOrWhiteSpace(section)));
    }

    private static List<string> BuildCvTugFlagLines(JsonElement assessment)
    {
        List<string> lines = [];

        if (TryGetProperty(assessment, "automated_flags", out JsonElement automatedFlags))
        {
            if (TryGetProperty(automatedFlags, "tug_above_upper_limit", out JsonElement tugUpperFlag))
            {
                string? value = ValueToString(tugUpperFlag);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    lines.Add($"- TUG acima do limite superior: {value}");
                }
            }

            if (TryGetProperty(automatedFlags, "fall_screening", out JsonElement fallScreening))
            {
                string? status = GetString(fallScreening, "status");
                if (!string.IsNullOrWhiteSpace(status))
                {
                    lines.Add($"- Triagem de quedas: {status}");
                }
            }

            if (TryGetProperty(automatedFlags, "dual_task_cost", out JsonElement dualTaskCost))
            {
                string? status = GetString(dualTaskCost, "status");
                string? percent = TryGetDoubleString(dualTaskCost, "worst_percent");

                if (!string.IsNullOrWhiteSpace(status) && !string.IsNullOrWhiteSpace(percent))
                {
                    lines.Add($"- Dual-task cost: {status} ({percent}%)");
                }
                else if (!string.IsNullOrWhiteSpace(status))
                {
                    lines.Add($"- Dual-task cost: {status}");
                }
            }

            if (TryGetProperty(automatedFlags, "gait_speed", out JsonElement gaitSpeed))
            {
                string? speed = TryGetDoubleString(gaitSpeed, "normal_condition_mps");
                string? note = GetString(gaitSpeed, "note");

                if (!string.IsNullOrWhiteSpace(speed))
                {
                    lines.Add($"- Velocidade media: {speed} m/s");
                }

                if (!string.IsNullOrWhiteSpace(note))
                {
                    lines.Add($"- Nota velocidade: {note}");
                }
            }
        }
        else if (TryGetProperty(assessment, "flags", out JsonElement flags))
        {
            AddLabeledValue(lines, "- Dual-task cost", GetString(flags, "dual_task_cost_status"));
            AddLabeledValue(lines, "- Velocidade media", TryGetDoubleString(flags, "normal_walk_speed_mps"), " m/s");
            AddLabeledValue(lines, "- Nota velocidade", GetString(flags, "walk_speed_note"));
        }

        return lines;
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

    private static string? TryGetIntString(JsonElement element, string key)
    {
        return TryGetProperty(element, key, out JsonElement value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt32(out int parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? TryGetDoubleString(JsonElement element, string key)
    {
        return TryGetProperty(element, key, out JsonElement value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetDouble(out double parsed)
            ? parsed.ToString("0.##", CultureInfo.InvariantCulture)
            : null;
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

    private static void AddLabeledValue(List<string> lines, string label, string? value, string suffix = "")
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{label}: {value}{suffix}");
        }
    }

    private static void AddPhaseValue(List<string> parts, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label} {value}s");
        }
    }

    private static string CreateStableRecordId(string payloadId, string sourceId, int index)
    {
        return $"{payloadId}:{sourceId}:{index + 1}";
    }
}
