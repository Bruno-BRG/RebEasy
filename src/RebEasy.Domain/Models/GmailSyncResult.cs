namespace RebEasy.Domain.Models;

public sealed class GmailSyncResult
{
    public string AccountEmail { get; init; } = string.Empty;
    public string? LastHistoryId { get; init; }
    public DateTimeOffset SyncedAt { get; init; }
    public IReadOnlyList<EmailMessage> Messages { get; init; } = Array.Empty<EmailMessage>();
}
