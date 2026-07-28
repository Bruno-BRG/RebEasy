using System.Text.Json;
using RehabEasy.Domain.Models;

namespace RehabEasy.Infrastructure.Services;

public static class PatientRecordHelper
{
    public const string TestTypeCvTug = "CvTUG";
    public const string TestTypeEquilibrio = "Equilibrio";
    public const string TestTypeOther = "Outro";

    public static string? TryGetPatientExternalId(string rawPayloadJson)
    {
        return TryGetPatientString(rawPayloadJson, "external_id");
    }

    public static string? TryGetPatientName(string rawPayloadJson)
    {
        return TryGetPatientString(rawPayloadJson, "name");
    }

    public static string ResolveTestType(string rawPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(rawPayloadJson))
        {
            return TestTypeOther;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawPayloadJson);
            JsonElement root = document.RootElement;

            string? explicitType = TryGetString(root, "test_type")
                ?? TryGetNestedString(root, "assessment", "test_type")
                ?? TryGetNestedString(root, "assessment", "type");

            if (!string.IsNullOrWhiteSpace(explicitType))
            {
                return NormalizeTestType(explicitType);
            }

            string? sender = TryGetString(root, "sender")
                ?? TryGetString(root, "source")
                ?? TryGetString(root, "source_system");

            if (string.Equals(sender, TestTypeCvTug, StringComparison.OrdinalIgnoreCase))
            {
                return TestTypeCvTug;
            }

            if (string.Equals(sender, "Posturografia VR", StringComparison.OrdinalIgnoreCase))
            {
                return TestTypeEquilibrio;
            }

            if (TryGetProperty(root, "assessment", out JsonElement assessment))
            {
                if (TryGetProperty(assessment, "posturographic_indices", out _))
                {
                    return TestTypeEquilibrio;
                }

                if (TryGetProperty(assessment, "conditions", out _) &&
                    TryGetProperty(root, "patient", out _))
                {
                    return TestTypeCvTug;
                }
            }

            return TestTypeOther;
        }
        catch (JsonException)
        {
            return TestTypeOther;
        }
    }

    public static string ResolvePatientId(RehabEasyRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.PatientId))
        {
            return record.PatientId.Trim();
        }

        return TryGetPatientExternalId(record.RawPayloadJson)?.Trim() ?? string.Empty;
    }

    public static string ResolveTestType(RehabEasyRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.TestType))
        {
            return record.TestType;
        }

        return ResolveTestType(record.RawPayloadJson);
    }

    private static string NormalizeTestType(string testType)
    {
        if (testType.Contains("tug", StringComparison.OrdinalIgnoreCase))
        {
            return TestTypeCvTug;
        }

        if (testType.Contains("equil", StringComparison.OrdinalIgnoreCase) ||
            testType.Contains("posturo", StringComparison.OrdinalIgnoreCase))
        {
            return TestTypeEquilibrio;
        }

        return testType.Trim();
    }

    private static string? TryGetPatientString(string rawPayloadJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawPayloadJson))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawPayloadJson);
            JsonElement root = document.RootElement;

            if (!TryGetProperty(root, "patient", out JsonElement patient))
            {
                return null;
            }

            return TryGetString(patient, propertyName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryGetNestedString(JsonElement root, string objectName, string propertyName)
    {
        return TryGetProperty(root, objectName, out JsonElement nested)
            ? TryGetString(nested, propertyName)
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

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out JsonElement value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
