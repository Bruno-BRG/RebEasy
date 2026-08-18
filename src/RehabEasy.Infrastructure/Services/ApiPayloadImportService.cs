using System.Net.Http;
using System.Net;
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
    private readonly string _pdfStorageDirectory;

    public ApiPayloadImportService(HttpClient httpClient, string systemBApiKey, string? pdfStorageDirectory = null)
    {
        _httpClient = httpClient;
        _systemBApiKey = systemBApiKey;
        _pdfStorageDirectory = pdfStorageDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RehabEasy",
                "pdfs");
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

        return await BuildImportResultAsync(response, cancellationToken);
    }

    public async Task<ApiPayloadImportResult?> ImportNextPayloadAsync(CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "api/payloads/next");
        request.Headers.Add("X-API-KEY", _systemBApiKey);

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"API retornou {(int)response.StatusCode}: {error}");
        }

        return await BuildImportResultAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ApiPayloadImportResult>> ImportAllPendingPayloadsAsync(
        CancellationToken cancellationToken)
    {
        const int maxPayloadsPerRefresh = 50;
        List<ApiPayloadImportResult> imported = [];

        while (imported.Count < maxPayloadsPerRefresh)
        {
            ApiPayloadImportResult? next = await ImportNextPayloadAsync(cancellationToken);
            if (next is null)
            {
                break;
            }

            imported.Add(next);
        }

        return imported;
    }

    private async Task<ApiPayloadImportResult> BuildImportResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ApiPayloadEnvelope? envelope = await response.Content.ReadFromJsonAsync<ApiPayloadEnvelope>(JsonOptions, cancellationToken);
        if (envelope?.Payload is null)
        {
            throw new InvalidOperationException("A API retornou um payload vazio ou invalido.");
        }

        DateTimeOffset importedAt = DateTimeOffset.UtcNow;
        string pdfLocalPath = string.Empty;
        if (!string.IsNullOrWhiteSpace(envelope.PdfUrl))
        {
            pdfLocalPath = await DownloadPdfAsync(envelope.Id, envelope.PdfUrl, cancellationToken);
        }

        IReadOnlyList<RehabEasyRecord> records = PayloadRecordMapper.Map(
            envelope.Id,
            envelope.Payload.Value,
            importedAt,
            pdfLocalPath);

        return new ApiPayloadImportResult
        {
            PayloadId = envelope.Id,
            SourceName = PayloadRecordMapper.GetSourceName(envelope.Payload.Value),
            ImportedAt = importedAt,
            PdfUrl = envelope.PdfUrl,
            PdfLocalPath = pdfLocalPath,
            Records = records
        };
    }

    private async Task<string> DownloadPdfAsync(string payloadId, string pdfUrl, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_pdfStorageDirectory);
        string safeName = string.Join("_", payloadId.Split(Path.GetInvalidFileNameChars()));
        string targetPath = Path.Combine(_pdfStorageDirectory, $"{safeName}.pdf");

        using HttpResponseMessage pdfResponse = await _httpClient.GetAsync(pdfUrl, cancellationToken);
        if (!pdfResponse.IsSuccessStatusCode)
        {
            string error = await pdfResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Falha ao baixar PDF: {(int)pdfResponse.StatusCode} {error}");
        }

        await using Stream source = await pdfResponse.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream target = new(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(target, cancellationToken);
        return targetPath;
    }

    private sealed class ApiPayloadEnvelope
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("payload")]
        public JsonElement? Payload { get; init; }

        [JsonPropertyName("pdf_url")]
        public string? PdfUrl { get; init; }
    }
}
