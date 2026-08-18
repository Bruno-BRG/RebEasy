package com.rehabeasy.json;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.NullNode;
import com.fasterxml.jackson.datatype.jsr310.JavaTimeModule;

import java.math.BigDecimal;
import java.time.Instant;
import java.time.LocalDateTime;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.time.format.DateTimeParseException;
import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;
import java.util.Locale;

public final class JsonSupport {
    public static final ObjectMapper MAPPER = new ObjectMapper()
            .registerModule(new JavaTimeModule());

    private JsonSupport() {
    }

    public static JsonNode readTree(String json) {
        try {
            return MAPPER.readTree(json);
        } catch (JsonProcessingException exception) {
            throw new IllegalArgumentException("JSON invalido.", exception);
        }
    }

    public static JsonNode property(JsonNode node, String... names) {
        if (node == null || node.isNull() || !node.isObject()) {
            return NullNode.getInstance();
        }

        for (String name : names) {
            Iterator<JsonNode> values = node.elements();
            Iterator<String> fields = node.fieldNames();
            while (fields.hasNext() && values.hasNext()) {
                String fieldName = fields.next();
                JsonNode value = values.next();
                if (fieldName.equalsIgnoreCase(name)) {
                    return value;
                }
            }
        }
        return NullNode.getInstance();
    }

    public static String string(JsonNode node, String... names) {
        for (String name : names) {
            JsonNode value = property(node, name);
            String result = scalarString(value);
            if (result != null && !result.isBlank()) {
                return result;
            }
        }
        return null;
    }

    public static String scalarString(JsonNode value) {
        if (value == null || value.isNull() || value.isContainerNode()) {
            return null;
        }
        if (value.isTextual() || value.isNumber() || value.isBoolean()) {
            return value.asText();
        }
        return null;
    }

    public static Double doubleValue(JsonNode node, String... names) {
        JsonNode value = property(node, names);
        if (value.isNumber()) {
            return value.doubleValue();
        }
        if (value.isTextual()) {
            try {
                return Double.parseDouble(value.textValue().replace(',', '.'));
            } catch (NumberFormatException ignored) {
                return null;
            }
        }
        return null;
    }

    public static Integer intValue(JsonNode node, String... names) {
        JsonNode value = property(node, names);
        if (value.isIntegralNumber()) {
            return value.intValue();
        }
        if (value.isTextual()) {
            try {
                return Integer.parseInt(value.textValue().trim());
            } catch (NumberFormatException ignored) {
                return null;
            }
        }
        return null;
    }

    public static Boolean booleanValue(JsonNode node, String... names) {
        JsonNode value = property(node, names);
        if (value.isBoolean()) {
            return value.booleanValue();
        }
        if (value.isTextual()) {
            return switch (value.textValue().trim().toLowerCase(Locale.ROOT)) {
                case "true", "sim", "yes", "verdadeiro" -> true;
                case "false", "nao", "não", "no", "falso" -> false;
                default -> null;
            };
        }
        return null;
    }

    public static List<JsonNode> elements(JsonNode node) {
        List<JsonNode> result = new ArrayList<>();
        if (node != null && node.isArray()) {
            node.elements().forEachRemaining(result::add);
        }
        return result;
    }

    public static List<String> stringArray(JsonNode node, String... names) {
        JsonNode value = property(node, names);
        List<String> result = new ArrayList<>();
        if (value.isArray()) {
            for (JsonNode item : value) {
                String text = scalarString(item);
                if (text != null && !text.isBlank()) {
                    result.add(text);
                }
            }
        } else {
            String text = scalarString(value);
            if (text != null && !text.isBlank()) {
                for (String part : text.split(",")) {
                    if (!part.isBlank()) {
                        result.add(part.trim());
                    }
                }
            }
        }
        return result;
    }

    public static Instant instant(JsonNode node, String... names) {
        String raw = string(node, names);
        if (raw == null || raw.isBlank()) {
            return null;
        }

        try {
            return Instant.parse(raw);
        } catch (DateTimeParseException ignored) {
        }
        try {
            return OffsetDateTime.parse(raw).toInstant();
        } catch (DateTimeParseException ignored) {
        }
        try {
            return LocalDateTime.parse(raw).toInstant(ZoneOffset.UTC);
        } catch (DateTimeParseException ignored) {
        }
        try {
            return Instant.ofEpochSecond(Long.parseLong(raw));
        } catch (NumberFormatException ignored) {
            return null;
        }
    }

    public static String formatDouble(Double value, String pattern) {
        if (value == null) {
            return "--";
        }
        java.text.DecimalFormat format = new java.text.DecimalFormat(
                pattern,
                java.text.DecimalFormatSymbols.getInstance(Locale.ROOT));
        return format.format(value);
    }

    public static String json(JsonNode node) {
        try {
            return MAPPER.writeValueAsString(node);
        } catch (JsonProcessingException exception) {
            throw new IllegalArgumentException("Nao foi possivel serializar JSON.", exception);
        }
    }

    public static BigDecimal decimal(JsonNode node, String... names) {
        JsonNode value = property(node, names);
        if (value.isNumber()) {
            return value.decimalValue();
        }
        return null;
    }
}
