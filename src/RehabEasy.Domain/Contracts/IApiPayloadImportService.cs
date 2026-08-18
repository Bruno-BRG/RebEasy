using RehabEasy.Domain.Models;

namespace RehabEasy.Domain.Contracts;

public interface IApiPayloadImportService
{
    Task<ApiPayloadImportResult> ImportPayloadAsync(string payloadId, CancellationToken cancellationToken);
    Task<ApiPayloadImportResult?> ImportNextPayloadAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ApiPayloadImportResult>> ImportAllPendingPayloadsAsync(CancellationToken cancellationToken);
}
