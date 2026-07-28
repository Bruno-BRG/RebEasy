using System.Text;
using System.Text.Json;
using RehabEasy.Domain.Models;

namespace RehabEasy.Infrastructure.Services;

public static class PatientTestMetricsExtractor
{
    public static string BuildMetricsSummary(string rawPayloadJson, string testType)
    {
        return testType switch
        {
            PatientRecordHelper.TestTypeCvTug => BuildCvTugSummary(rawPayloadJson),
            PatientRecordHelper.TestTypeEquilibrio => BuildEquilibrioSummary(rawPayloadJson),
            _ => "Indicadores nao mapeados para este tipo de teste."
        };
    }

    public static string BuildDetailText(RehabEasyRecord record)
    {
        string testType = PatientRecordHelper.ResolveTestType(record);
        StringBuilder builder = new();
        builder.AppendLine($"Tipo de teste: {testType}");
        builder.AppendLine($"Titulo: {record.Title}");
        builder.AppendLine($"Origem: {record.Sender}");
        builder.AppendLine($"Recebido em: {record.ReceivedAt.LocalDateTime:g}");
        builder.AppendLine($"Indicadores: {BuildMetricsSummary(record.RawPayloadJson, testType)}");

        if (!string.IsNullOrWhiteSpace(record.Summary))
        {
            builder.AppendLine($"Resumo: {record.Summary}");
        }

        builder.AppendLine();
        builder.AppendLine(string.IsNullOrWhiteSpace(record.PlainTextContent)
            ? record.RawPayloadJson
            : record.PlainTextContent);

        return builder.ToString().TrimEnd();
    }

    private static string BuildCvTugSummary(string rawPayloadJson)
    {
        List<string> parts = [];

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawPayloadJson);
            JsonElement root = document.RootElement;

            if (TryGetProperty(root, "assessment", out JsonElement assessment))
            {
                if (TryGetProperty(assessment, "conditions", out JsonElement conditions) &&
                    conditions.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement condition in conditions.EnumerateArray())
                    {
                        string? code = TryGetString(condition, "code");
                        double? totalSeconds = TryGetDouble(condition, "total_seconds");
                        if (totalSeconds is double seconds)
                        {
                            parts.Add($"{code ?? "condicao"} {seconds:0.0}s");
                        }
                    }
                }

                if (TryGetProperty(assessment, "derived_metrics", out JsonElement derivedMetrics))
                {
                    AddDoubleMetric(parts, "DTC pior", TryGetDouble(derivedMetrics, "worst_dual_task_cost_percent"), "%");
                    AddDoubleMetric(parts, "Velocidade", TryGetDouble(derivedMetrics, "normal_walk_speed_mps"), " m/s");
                }

                if (TryGetProperty(assessment, "automated_flags", out JsonElement automatedFlags) &&
                    TryGetProperty(automatedFlags, "dual_task_cost", out JsonElement dualTaskCost))
                {
                    string? status = TryGetString(dualTaskCost, "status");
                    if (!string.IsNullOrWhiteSpace(status))
                    {
                        parts.Add($"Alerta DTC: {status}");
                    }
                }
            }
        }
        catch (JsonException)
        {
            return "Nao foi possivel extrair indicadores CvTUG.";
        }

        return parts.Count == 0 ? "Sem indicadores CvTUG disponiveis." : string.Join(" | ", parts);
    }

    private static string BuildEquilibrioSummary(string rawPayloadJson)
    {
        List<string> parts = [];

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawPayloadJson);
            JsonElement root = document.RootElement;

            if (TryGetProperty(root, "assessment", out JsonElement assessment))
            {
                if (TryGetProperty(assessment, "derived_metrics", out JsonElement derivedMetrics))
                {
                    AddDoubleMetric(parts, "SPL", TryGetDouble(derivedMetrics, "spl_mm"), " mm");
                    AddDoubleMetric(
                        parts,
                        "Velocidade osc.",
                        TryGetDouble(derivedMetrics, "mean_oscillation_velocity_mm_s"),
                        " mm/s");
                    AddDoubleMetric(parts, "Romberg", TryGetDouble(derivedMetrics, "romberg_area_quotient"), string.Empty);
                }

                if (TryGetProperty(assessment, "automated_flags", out JsonElement automatedFlags) &&
                    TryGetProperty(automatedFlags, "visual_dependency", out JsonElement visualDependency))
                {
                    string? status = TryGetString(visualDependency, "status");
                    if (!string.IsNullOrWhiteSpace(status))
                    {
                        parts.Add($"Dependencia visual: {status}");
                    }
                }

                string? interpretation = TryGetString(assessment, "interpretation");
                if (!string.IsNullOrWhiteSpace(interpretation))
                {
                    parts.Add(interpretation);
                }
            }
        }
        catch (JsonException)
        {
            return "Nao foi possivel extrair indicadores de equilibrio.";
        }

        return parts.Count == 0 ? "Sem indicadores de equilibrio disponiveis." : string.Join(" | ", parts);
    }

    private static void AddDoubleMetric(List<string> parts, string label, double? value, string suffix)
    {
        if (value is double parsed)
        {
            parts.Add($"{label} {parsed:0.##}{suffix}".Trim());
        }
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

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out JsonElement value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double parsed)
            ? parsed
            : null;
    }
}
