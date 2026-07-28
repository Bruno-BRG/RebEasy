namespace RehabEasy.Domain.Models;

public sealed class RehabEasyRecord
{
    public string Id { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Sender { get; init; } = string.Empty;
    public string Recipient { get; init; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string PlainTextContent { get; init; } = string.Empty;
    public string HtmlContent { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public string RawPayloadJson { get; init; } = "{}";
    public string PatientId { get; init; } = string.Empty;
    public string TestType { get; init; } = string.Empty;
    public string PdfLocalPath { get; init; } = string.Empty;
}
