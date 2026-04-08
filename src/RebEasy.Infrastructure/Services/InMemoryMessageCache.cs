using RebEasy.Domain.Contracts;
using RebEasy.Domain.Models;

namespace RebEasy.Infrastructure.Services;

public sealed class InMemoryMessageCache : IMessageCache
{
    private readonly List<EmailMessage> _messages = [];
    private SyncState? _state;

    public Task SaveMessagesAsync(IEnumerable<EmailMessage> messages, CancellationToken cancellationToken)
    {
        _messages.Clear();
        _messages.AddRange(messages.OrderByDescending(message => message.ReceivedAt));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EmailMessage>> SearchAsync(string? query, CancellationToken cancellationToken)
    {
        IEnumerable<EmailMessage> result = _messages;

        if (!string.IsNullOrWhiteSpace(query))
        {
            result = result.Where(message =>
                message.Subject.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                message.From.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                message.Snippet.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult<IReadOnlyList<EmailMessage>>(result.ToList());
    }

    public Task<SyncState?> GetSyncStateAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_state);
    }

    public Task SaveSyncStateAsync(SyncState state, CancellationToken cancellationToken)
    {
        _state = state;
        return Task.CompletedTask;
    }
}
