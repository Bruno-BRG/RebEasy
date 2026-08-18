package com.rehabeasy.integration;

import com.fasterxml.jackson.databind.JsonNode;
import com.rehabeasy.AppConfig;
import com.rehabeasy.json.JsonSupport;
import com.rehabeasy.model.ApiPayloadImportResult;
import com.rehabeasy.model.RehabEasyRecord;
import com.rehabeasy.service.PayloadImportService;
import com.rehabeasy.service.PayloadRecordMapper;

import java.io.IOException;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;

public final class ApiPayloadImportService implements PayloadImportService {
    private static final int MAX_PAYLOADS_PER_REFRESH = 50;

    private final HttpClient httpClient;
    private final URI baseUri;
    private final String systemBApiKey;
    private final Path pdfStorageDirectory;

    public ApiPayloadImportService(AppConfig config) {
        this(
                HttpClient.newBuilder()
                        .connectTimeout(Duration.ofSeconds(20))
                        .followRedirects(HttpClient.Redirect.NORMAL)
                        .build(),
                config.apiBaseUri(),
                config.systemBApiKey(),
                config.pdfDirectory());
    }

    public ApiPayloadImportService(
            HttpClient httpClient,
            URI baseUri,
            String systemBApiKey,
            Path pdfStorageDirectory
    ) {
        this.httpClient = httpClient;
        this.baseUri = baseUri;
        this.systemBApiKey = systemBApiKey;
        this.pdfStorageDirectory = pdfStorageDirectory;
    }

    @Override
    public ApiPayloadImportResult importPayload(String payloadId) {
        if (payloadId == null || payloadId.isBlank()) {
            throw new IllegalArgumentException("Informe o ID do payload para importar.");
        }
        HttpResponse<String> response = send(
                "GET",
                "api/payloads/" + encodePathSegment(payloadId.trim()),
                null);
        requireSuccess(response);
        return buildImportResult(response.body());
    }

    @Override
    public ApiPayloadImportResult importNextPayload() {
        HttpResponse<String> response = send("GET", "api/payloads/next", null);
        if (response.statusCode() == 404) {
            return null;
        }
        requireSuccess(response);
        return buildImportResult(response.body());
    }

    @Override
    public List<ApiPayloadImportResult> importAllPendingPayloads() {
        List<ApiPayloadImportResult> imported = new ArrayList<>();
        while (imported.size() < MAX_PAYLOADS_PER_REFRESH) {
            ApiPayloadImportResult next = importNextPayload();
            if (next == null) {
                break;
            }
            imported.add(next);
        }
        return imported;
    }

    private ApiPayloadImportResult buildImportResult(String responseBody) {
        JsonNode envelope = JsonSupport.readTree(responseBody);
        String payloadId = JsonSupport.string(envelope, "id");
        JsonNode payload = JsonSupport.property(envelope, "payload");
        if (payloadId == null || payload.isNull() || payload.isMissingNode()) {
            throw new ApiImportException("A API retornou um payload vazio ou invalido.");
        }

        Instant importedAt = Instant.now();
        String pdfUrl = JsonSupport.string(envelope, "pdf_url");
        String pdfLocalPath = "";
        if (pdfUrl != null && !pdfUrl.isBlank()) {
            pdfLocalPath = downloadPdf(payloadId, pdfUrl);
        }

        List<RehabEasyRecord> records = PayloadRecordMapper.map(
                payloadId,
                payload,
                importedAt,
                pdfLocalPath);
        return new ApiPayloadImportResult(
                payloadId,
                PayloadRecordMapper.getSourceName(payload),
                importedAt,
                pdfUrl,
                pdfLocalPath,
                records);
    }

    private String downloadPdf(String payloadId, String pdfUrl) {
        URI uri;
        try {
            uri = URI.create(pdfUrl);
        } catch (IllegalArgumentException exception) {
            throw new ApiImportException("URL do PDF invalida.", exception);
        }
        if (!"http".equalsIgnoreCase(uri.getScheme()) && !"https".equalsIgnoreCase(uri.getScheme())) {
            throw new ApiImportException("URL do PDF precisa usar HTTP ou HTTPS.");
        }

        HttpRequest request = HttpRequest.newBuilder(uri)
                .timeout(Duration.ofMinutes(2))
                .GET()
                .build();
        try {
            HttpResponse<byte[]> response = httpClient.send(request, HttpResponse.BodyHandlers.ofByteArray());
            if (response.statusCode() < 200 || response.statusCode() >= 300) {
                throw new ApiImportException(
                        "Falha ao baixar PDF: " + response.statusCode());
            }
            Files.createDirectories(pdfStorageDirectory);
            String safeName = payloadId.replaceAll("[^a-zA-Z0-9._-]", "_");
            Path target = pdfStorageDirectory.resolve(safeName + ".pdf");
            Files.write(target, response.body());
            return target.toString();
        } catch (IOException exception) {
            throw new ApiImportException("Falha ao salvar o PDF localmente.", exception);
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            throw new ApiImportException("Download do PDF interrompido.", exception);
        }
    }

    private HttpResponse<String> send(String method, String relativePath, String body) {
        URI uri = baseUri.resolve(relativePath);
        HttpRequest.Builder builder = HttpRequest.newBuilder(uri)
                .timeout(Duration.ofMinutes(2))
                .header("X-API-KEY", systemBApiKey)
                .header("Accept", "application/json");
        if (body == null) {
            builder.method(method, HttpRequest.BodyPublishers.noBody());
        } else {
            builder.header("Content-Type", "application/json");
            builder.method(method, HttpRequest.BodyPublishers.ofString(body));
        }

        try {
            return httpClient.send(builder.build(), HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
        } catch (IOException exception) {
            throw new ApiImportException("Falha de comunicacao com a API.", exception);
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            throw new ApiImportException("Comunicacao com a API interrompida.", exception);
        }
    }

    private static void requireSuccess(HttpResponse<String> response) {
        if (response.statusCode() < 200 || response.statusCode() >= 300) {
            String error = response.body() == null ? "" : response.body();
            throw new ApiImportException(
                    "API retornou " + response.statusCode() + ": " + error);
        }
    }

    private static String encodePathSegment(String value) {
        return URLEncoder.encode(value, StandardCharsets.UTF_8).replace("+", "%20");
    }

    public static final class ApiImportException extends RuntimeException {
        public ApiImportException(String message) {
            super(message);
        }

        public ApiImportException(String message, Throwable cause) {
            super(message, cause);
        }
    }
}
