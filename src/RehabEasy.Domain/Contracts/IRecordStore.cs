using RehabEasy.Domain.Models;

namespace RehabEasy.Domain.Contracts;

public interface IRecordStore
{
    Task SaveRecordsAsync(IEnumerable<RehabEasyRecord> records, CancellationToken cancellationToken);
    Task<IReadOnlyList<RehabEasyRecord>> SearchAsync(string? query, CancellationToken cancellationToken);
    Task DeleteRecordAsync(string id, CancellationToken cancellationToken);
}
