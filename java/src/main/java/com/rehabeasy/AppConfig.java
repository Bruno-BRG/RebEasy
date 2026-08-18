package com.rehabeasy;

import java.net.URI;
import java.net.URISyntaxException;
import java.nio.file.Path;

public record AppConfig(
        URI apiBaseUri,
        String systemBApiKey,
        Path databasePath,
        Path pdfDirectory
) {
    public static final String API_BASE_URL_ENV = "REHABEASY_API_BASE_URL";
    public static final String SYSTEM_B_API_KEY_ENV = "REHABEASY_SYSTEM_B_API_KEY";
    public static final String DEFAULT_API_BASE_URL = "https://telemedicinacc.vercel.app/";
    public static final String DEFAULT_SYSTEM_B_API_KEY = "rehabeasy-system-b";

    public static AppConfig fromEnvironment() {
        String baseUrl = environmentOrDefault(API_BASE_URL_ENV, DEFAULT_API_BASE_URL);
        String apiKey = environmentOrDefault(SYSTEM_B_API_KEY_ENV, DEFAULT_SYSTEM_B_API_KEY);
        if (apiKey.isBlank()) {
            throw new IllegalArgumentException(
                    "API key ausente. Defina " + SYSTEM_B_API_KEY_ENV + " com a chave do Sistema B.");
        }

        URI baseUri;
        try {
            baseUri = normalizeBaseUri(URI.create(baseUrl.trim()));
        } catch (IllegalArgumentException exception) {
            throw new IllegalArgumentException(
                    "URL da API invalida em " + API_BASE_URL_ENV + ": " + baseUrl, exception);
        }

        Path localApplicationData = localApplicationDataDirectory();
        Path appDirectory = localApplicationData.resolve("RehabEasy");
        return new AppConfig(
                baseUri,
                apiKey,
                appDirectory.resolve("rehabeasy.db"),
                appDirectory.resolve("pdfs"));
    }

    private static String environmentOrDefault(String name, String fallback) {
        String value = System.getenv(name);
        return value == null || value.isBlank() ? fallback : value;
    }

    private static URI normalizeBaseUri(URI uri) {
        if (!uri.isAbsolute() || uri.getHost() == null) {
            throw new IllegalArgumentException("A URL da API precisa ser absoluta.");
        }

        String path = uri.getPath() == null ? "/" : uri.getPath();
        if (!path.endsWith("/")) {
            path += "/";
        }

        try {
            return new URI(uri.getScheme(), uri.getUserInfo(), uri.getHost(), uri.getPort(), path, null, null);
        } catch (URISyntaxException exception) {
            throw new IllegalArgumentException("URL da API invalida: " + uri, exception);
        }
    }

    private static Path localApplicationDataDirectory() {
        String windowsPath = System.getenv("LOCALAPPDATA");
        if (windowsPath != null && !windowsPath.isBlank()) {
            return Path.of(windowsPath);
        }

        String home = System.getProperty("user.home");
        if (System.getProperty("os.name", "").toLowerCase().contains("win")) {
            return Path.of(home, "AppData", "Local");
        }
        return Path.of(home, ".local", "share");
    }
}
