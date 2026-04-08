using RebEasy.Domain.Models;

namespace RebEasy.Domain.Contracts;

public interface IMessageCache
{
    Task SaveMessagesAsync(IEnumerable<EmailMessage> messages, CancellationToken cancellationToken);
    Task<IReadOnlyList<EmailMessage>> SearchAsync(string? query, CancellationToken cancellationToken);
    Task<SyncState?> GetSyncStateAsync(CancellationToken cancellationToken);
    Task SaveSyncStateAsync(SyncState state, CancellationToken cancellationToken);
}
