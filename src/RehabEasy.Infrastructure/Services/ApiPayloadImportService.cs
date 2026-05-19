using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using RehabEasy.Domain.Contracts;
using RehabEasy.Domain.Models;

namespace RehabEasy.Infrastructure.Services;

public sealed class ApiPayloadImportService : IApiPayloadImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly HttpClient _httpClient;
    private readonly string _systemBApiKey;

    public ApiPayloadImportService(HttpClient httpClient, string systemBApiKey)
    {
        _httpClient = httpClient;
        _systemBApiKey = systemBApiKey;
    }

    public async Task<ApiPayloadImportResult> ImportPayloadAsync(string payloadId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payloadId))
        {
            throw new InvalidOperationException("Informe o ID do payload para importar.");
        }

        using HttpRequestMessage request = new(HttpMethod.Get, $"api/payloads/{Uri.EscapeDataString(payloadId.Trim())}");
        request.Headers.Add("X-API-KEY", _systemBApiKey);

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"API retornou {(int)response.StatusCode}: {error}");
        }

        ApiPayloadEnvelope? envelope = await response.Content.ReadFromJsonAsync<ApiPayloadEnvelope>(JsonOptions, cancellationToken);
        if (envelope?.Payload is null)
        {
            throw new InvalidOperationException("A API retornou um payload vazio ou invalido.");
        }

        DateTimeOffset importedAt = DateTimeOffset.UtcNow;
        IReadOnlyList<RehabEasyRecord> records = PayloadRecordMapper.Map(envelope.Id, envelope.Payload, importedAt);

        return new ApiPayloadImportResult
        {
            PayloadId = envelope.Id,
            SourceName = PayloadRecordMapper.GetSourceName(envelope.Payload),
            ImportedAt = importedAt,
            Records = records
        };
    }

    private sealed class ApiPayloadEnvelope
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("payload")]
        public JsonElement Payload { get; init; }
    }
}
