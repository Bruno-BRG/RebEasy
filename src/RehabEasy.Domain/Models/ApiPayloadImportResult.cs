namespace RehabEasy.Domain.Models;

public sealed class ApiPayloadImportResult
{
    public string PayloadId { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public DateTimeOffset ImportedAt { get; init; }
    public string? PdfUrl { get; init; }
    public string PdfLocalPath { get; init; } = string.Empty;
    public IReadOnlyList<RehabEasyRecord> Records { get; init; } = Array.Empty<RehabEasyRecord>();
}
