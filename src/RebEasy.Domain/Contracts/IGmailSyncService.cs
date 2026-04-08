using RebEasy.Domain.Models;

namespace RebEasy.Domain.Contracts;

public interface IGmailSyncService
{
    Task<GmailSyncResult> RunInitialSyncAsync(string? accountEmail, CancellationToken cancellationToken);
    Task<GmailSyncResult> RunIncrementalSyncAsync(SyncState state, CancellationToken cancellationToken);
}
