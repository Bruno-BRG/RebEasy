namespace RebEasy.Domain.Models;

public sealed class SyncState
{
    public string AccountEmail { get; init; } = string.Empty;
    public string? LastHistoryId { get; init; }
    public DateTimeOffset? LastSyncedAt { get; init; }
}
