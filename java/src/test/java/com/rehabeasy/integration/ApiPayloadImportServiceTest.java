package com.rehabeasy.integration;

import com.rehabeasy.model.ApiPayloadImportResult;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.net.InetSocketAddress;
import java.net.URI;
import java.net.http.HttpClient;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.util.concurrent.atomic.AtomicReference;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;

class ApiPayloadImportServiceTest {
    @Test
    void consumesNextPayloadWithSystemBHeader(@org.junit.jupiter.api.io.TempDir Path tempDirectory)
            throws IOException {
        AtomicReference<String> receivedApiKey = new AtomicReference<>();
        HttpServer server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        server.createContext("/api/payloads/next", exchange -> {
            receivedApiKey.set(exchange.getRequestHeaders().getFirst("X-API-KEY"));
            respond(exchange, 200, """
                    {
                      "id": "payload_test",
                      "payload": {
                        "source": "test",
                        "records": [{
                          "id": "record-1",
                          "title": "Registro de teste",
                          "created_at": "2026-08-17T12:00:00Z",
                          "content": "Conteudo"
                        }]
                      },
                      "pdf_url": null
                    }
                    """);
        });
        server.start();
        try {
            ApiPayloadImportService service = new ApiPayloadImportService(
                    HttpClient.newHttpClient(),
                    URI.create("http://127.0.0.1:" + server.getAddress().getPort() + "/"),
                    "system-b-test",
                    tempDirectory.resolve("pdfs"));

            ApiPayloadImportResult result = service.importNextPayload();

            assertNotNull(result);
            assertEquals("payload_test", result.payloadId());
            assertEquals("system-b-test", receivedApiKey.get());
            assertEquals("Registro de teste", result.records().getFirst().title());
        } finally {
            server.stop(0);
        }
    }

    @Test
    void mapsNoPendingPayloadToNull(@org.junit.jupiter.api.io.TempDir Path tempDirectory) throws IOException {
        HttpServer server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        server.createContext("/api/payloads/next", exchange -> respond(exchange, 404, "{\"detail\":\"none\"}"));
        server.start();
        try {
            ApiPayloadImportService service = new ApiPayloadImportService(
                    HttpClient.newHttpClient(),
                    URI.create("http://127.0.0.1:" + server.getAddress().getPort() + "/"),
                    "system-b-test",
                    tempDirectory.resolve("pdfs"));

            assertNull(service.importNextPayload());
        } finally {
            server.stop(0);
        }
    }

    private static void respond(HttpExchange exchange, int status, String body) throws IOException {
        byte[] bytes = body.getBytes(StandardCharsets.UTF_8);
        exchange.getResponseHeaders().set("Content-Type", "application/json");
        exchange.sendResponseHeaders(status, bytes.length);
        try (exchange) {
            exchange.getResponseBody().write(bytes);
        }
    }
}
