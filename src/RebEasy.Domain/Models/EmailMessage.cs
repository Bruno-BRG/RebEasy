namespace RebEasy.Domain.Models;

public sealed class EmailMessage
{
    public string Id { get; init; } = string.Empty;
    public string ThreadId { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; init; }
    public string Snippet { get; init; } = string.Empty;
    public string PlainTextBody { get; init; } = string.Empty;
    public string HtmlBody { get; init; } = string.Empty;
    public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();
}
