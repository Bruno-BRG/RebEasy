package com.rehabeasy.service;

import com.fasterxml.jackson.databind.JsonNode;
import com.rehabeasy.json.JsonSupport;
import com.rehabeasy.model.RehabEasyRecord;

import java.time.Instant;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Locale;

public final class PayloadRecordMapper {
    private static final String[] RECORD_ARRAY_KEYS = {"records", "registros", "items", "itens", "data", "dados"};
    private static final String[] ID_KEYS = {"id", "record_id", "registro_id", "external_id", "source_id"};
    private static final String[] TITLE_KEYS = {"title", "titulo", "subject", "assunto", "name", "nome", "evento", "tipo"};
    private static final String[] SENDER_KEYS = {"sender", "source", "source_system", "from", "remetente", "origem"};
    private static final String[] RECIPIENT_KEYS = {"recipient", "destination", "to", "destinatario", "destino"};
    private static final String[] DATE_KEYS = {"received_at", "created_at", "updated_at", "data", "date", "timestamp"};
    private static final String[] SUMMARY_KEYS = {"summary", "resumo", "snippet", "descricao", "description"};
    private static final String[] CONTENT_KEYS = {"content", "body", "plain_text", "plainTextBody", "mensagem", "observacoes", "notes"};
    private static final String[] HTML_KEYS = {"html", "html_body", "htmlBody"};
    private static final String[] TAGS_KEYS = {"tags", "labels", "etiquetas", "categorias"};

    private PayloadRecordMapper() {
    }

    public static List<RehabEasyRecord> map(
            String payloadId,
            JsonNode payload,
            Instant importedAt,
            String pdfLocalPath
    ) {
        List<JsonNode> sourceRecords = extractRecordElements(payload);
        if (sourceRecords.isEmpty() && payload != null && payload.isObject()) {
            sourceRecords.add(payload);
        }

        List<RehabEasyRecord> mappedRecords = new ArrayList<>();
        for (int index = 0; index < sourceRecords.size(); index++) {
            JsonNode record = sourceRecords.get(index);
            if (record.isObject()) {
                mappedRecords.add(mapRecord(payloadId, record, index, importedAt, pdfLocalPath));
            }
        }
        return mappedRecords.stream()
                .sorted(Comparator.comparing(RehabEasyRecord::receivedAt).reversed())
                .toList();
    }

    public static String getSourceName(JsonNode payload) {
        String source = JsonSupport.string(payload, "source", "source_system", "sistema", "origem");
        return source == null ? "api" : source;
    }

    private static RehabEasyRecord mapRecord(
            String payloadId,
            JsonNode record,
            int index,
            Instant importedAt,
            String pdfLocalPath
    ) {
        String sourceId = value(record, ID_KEYS);
        if (sourceId == null) {
            sourceId = payloadId + ":" + (index + 1);
        }
        String title = value(record, TITLE_KEYS);
        if (title == null) {
            title = "Registro " + (index + 1);
        }
        String summary = defaultString(value(record, SUMMARY_KEYS));
        String rawJson = JsonSupport.json(record);
        return new RehabEasyRecord(
                createStableRecordId(payloadId, sourceId, index),
                sourceId,
                title,
                defaultValue(value(record, SENDER_KEYS), "api"),
                defaultValue(value(record, RECIPIENT_KEYS), "RehabEasy"),
                date(record, importedAt),
                summary,
                buildPlainTextContent(record, summary, rawJson),
                defaultString(value(record, HTML_KEYS)),
                JsonSupport.stringArray(record, TAGS_KEYS),
                rawJson,
                defaultString(PatientRecordHelper.tryGetPatientExternalId(rawJson)),
                PatientRecordHelper.resolveTestType(rawJson),
                defaultString(pdfLocalPath)
        );
    }

    private static String buildPlainTextContent(JsonNode record, String summary, String rawJson) {
        if (isIndexIndexRecord(record)) {
            String content = buildIndexIndexPlainTextContent(record, summary);
            if (!content.isBlank()) {
                return content;
            }
        }
        if (isEquilibrioRecord(record)) {
            String content = buildEquilibrioPlainTextContent(record, summary);
            if (!content.isBlank()) {
                return content;
            }
        }
        if (isCvTugRecord(record)) {
            String content = buildCvTugPlainTextContent(record, summary);
            if (!content.isBlank()) {
                return content;
            }
        }
        String content = value(record, CONTENT_KEYS);
        return content == null ? summary : content;
    }

    private static boolean isIndexIndexRecord(JsonNode record) {
        String sender = value(record, SENDER_KEYS);
        if ("Index-Index".equalsIgnoreCase(sender) || "index-index".equalsIgnoreCase(sender)) {
            return true;
        }
        JsonNode assessment = JsonSupport.property(record, "assessment");
        String testType = JsonSupport.string(assessment, "test_type");
        if (testType != null && testType.toLowerCase(Locale.ROOT).contains("index")) {
            return true;
        }
        return has(assessment, "metrics") && has(JsonSupport.property(assessment, "metrics"), "final_fingertip_distance_mm");
    }

    private static boolean isEquilibrioRecord(JsonNode record) {
        String sender = value(record, SENDER_KEYS);
        if ("Posturografia VR".equalsIgnoreCase(sender)) {
            return true;
        }
        return has(JsonSupport.property(record, "assessment"), "posturographic_indices");
    }

    private static boolean isCvTugRecord(JsonNode record) {
        String sender = value(record, SENDER_KEYS);
        if ("CvTUG".equalsIgnoreCase(sender)) {
            return true;
        }
        JsonNode assessment = JsonSupport.property(record, "assessment");
        return has(assessment, "conditions") && has(record, "patient");
    }

    private static String buildCvTugPlainTextContent(JsonNode record, String summary) {
        List<String> sections = new ArrayList<>();
        addSection(sections, summary);
        addSection(sections, value(record, CONTENT_KEYS));

        JsonNode patient = JsonSupport.property(record, "patient");
        List<String> patientParts = new ArrayList<>();
        addLabeledValue(patientParts, "Paciente", JsonSupport.string(patient, "name"));
        addLabeledValue(patientParts, "Idade", intString(patient, "age_years"));
        addLabeledValue(patientParts, "Sexo", JsonSupport.string(patient, "sex"));
        addLabeledValue(patientParts, "ID Externo", JsonSupport.string(patient, "external_id"));
        if (!patientParts.isEmpty()) {
            sections.add("Paciente:\n" + String.join("\n", patientParts));
        }

        JsonNode assessment = JsonSupport.property(record, "assessment");
        List<String> metricLines = new ArrayList<>();
        addLabeledValue(metricLines, "Data do exame", JsonSupport.string(assessment, "performed_at"));
        JsonNode conditions = JsonSupport.property(assessment, "conditions");
        if (conditions.isArray()) {
            for (JsonNode condition : conditions) {
                String label = defaultValue(JsonSupport.string(condition, "label", "code"), "Condicao");
                String total = doubleString(condition, "total_seconds");
                if (total == null) {
                    total = "--";
                }
                String dtc = doubleString(condition, "dual_task_cost_percent");
                List<String> phaseParts = new ArrayList<>();
                JsonNode phases = JsonSupport.property(condition, "phases");
                addPhaseValue(phaseParts, "Levantar", doubleString(phases, "stand_seconds"));
                addPhaseValue(phaseParts, "Marcha", doubleString(phases, "walk_seconds"));
                addPhaseValue(phaseParts, "Sentar", doubleString(phases, "sit_seconds"));

                String line = "- " + label + ": total " + total + "s";
                if (dtc != null) {
                    line += "; DTC " + dtc + "%";
                }
                if (!phaseParts.isEmpty()) {
                    line += "; " + String.join("; ", phaseParts);
                }
                metricLines.add(line);
            }
        }
        if (!metricLines.isEmpty()) {
            sections.add("Resultados:\n" + String.join("\n", metricLines));
        }

        List<String> flagLines = buildCvTugFlagLines(assessment);
        if (!flagLines.isEmpty()) {
            sections.add("Sinalizadores:\n" + String.join("\n", flagLines));
        }
        List<String> notes = stringArray(assessment, "methodology_notes");
        if (!notes.isEmpty()) {
            sections.add("Notas metodologicas:\n" + String.join("\n", notes.stream().map("- "::concat).toList()));
        }
        return joinSections(sections);
    }

    private static String buildIndexIndexPlainTextContent(JsonNode record, String summary) {
        List<String> sections = new ArrayList<>();
        addSection(sections, summary);
        addSection(sections, value(record, CONTENT_KEYS));

        JsonNode patient = JsonSupport.property(record, "patient");
        List<String> patientParts = new ArrayList<>();
        addLabeledValue(patientParts, "Paciente", JsonSupport.string(patient, "name"));
        addLabeledValue(patientParts, "Idade", intString(patient, "age_years"));
        addLabeledValue(patientParts, "Sexo", JsonSupport.string(patient, "sex"));
        addLabeledValue(patientParts, "ID Exame", JsonSupport.string(patient, "external_id"));
        if (!patientParts.isEmpty()) {
            sections.add("Paciente:\n" + String.join("\n", patientParts));
        }

        JsonNode assessment = JsonSupport.property(record, "assessment");
        List<String> headerLines = new ArrayList<>();
        addLabeledValue(headerLines, "Data do exame", JsonSupport.string(assessment, "performed_at"));
        addLabeledValue(headerLines, "ID exame", JsonSupport.string(assessment, "exam_id"));
        JsonNode protocol = JsonSupport.property(assessment, "protocol");
        addLabeledValue(headerLines, "Protocolo", JsonSupport.string(protocol, "description"));
        addLabeledValue(headerLines, "Criterio", JsonSupport.string(protocol, "closing_criterion"));
        addLabeledValue(headerLines, "Limiar de toque", doubleString(protocol, "touch_threshold_mm"), " mm");
        if (!headerLines.isEmpty()) {
            sections.add(String.join("\n", headerLines));
        }

        JsonNode metrics = JsonSupport.property(assessment, "metrics");
        List<String> metricLines = new ArrayList<>();
        addLabeledValue(metricLines, "- Distancia final", doubleString(metrics, "final_fingertip_distance_mm"), " mm");
        addLabeledValue(metricLines, "- Duracao", doubleString(metrics, "movement_duration_seconds"), " s");
        addLabeledValue(metricLines, "- Oscilacao esquerda (DP)", doubleString(metrics, "left_hand_oscillation_sd_mm"), " mm");
        addLabeledValue(metricLines, "- Oscilacao direita (DP)", doubleString(metrics, "right_hand_oscillation_sd_mm"), " mm");
        addLabeledValue(metricLines, "- Oscilacao geral (DP)", doubleString(metrics, "overall_oscillation_sd_mm"), " mm");
        if (!metricLines.isEmpty()) {
            sections.add("Metricas:\n" + String.join("\n", metricLines));
        }

        List<String> flagLines = buildIndexIndexFlagLines(assessment);
        if (!flagLines.isEmpty()) {
            sections.add("Sinalizadores:\n" + String.join("\n", flagLines));
        }
        String interpretation = JsonSupport.string(assessment, "interpretation");
        if (interpretation != null) {
            sections.add("Interpretacao:\n" + interpretation.trim());
        }
        return joinSections(sections);
    }

    private static List<String> buildIndexIndexFlagLines(JsonNode assessment) {
        List<String> lines = new ArrayList<>();
        JsonNode flags = JsonSupport.property(assessment, "automated_flags");
        String touch = JsonSupport.scalarString(JsonSupport.property(flags, "touch_within_threshold"));
        if (touch != null) {
            lines.add("- Toque dentro do limiar: " + touch);
        }

        JsonNode asymmetry = JsonSupport.property(flags, "hand_asymmetry");
        String status = JsonSupport.string(asymmetry, "status");
        String ratio = doubleString(asymmetry, "ratio");
        String side = JsonSupport.string(asymmetry, "dominant_side");
        if (status != null && ratio != null) {
            String sideLabel = "right".equalsIgnoreCase(side)
                    ? "direita"
                    : "left".equalsIgnoreCase(side) ? "esquerda" : defaultValue(side, "--");
            lines.add("- Assimetria entre maos: " + status + " (razao " + ratio + "; predominio " + sideLabel + ")");
        } else if (status != null) {
            lines.add("- Assimetria entre maos: " + status);
        }
        return lines;
    }

    private static String buildEquilibrioPlainTextContent(JsonNode record, String summary) {
        List<String> sections = new ArrayList<>();
        addSection(sections, summary);
        addSection(sections, value(record, CONTENT_KEYS));

        JsonNode patient = JsonSupport.property(record, "patient");
        List<String> patientParts = new ArrayList<>();
        addLabeledValue(patientParts, "Paciente", JsonSupport.string(patient, "name"));
        addLabeledValue(patientParts, "Idade", intString(patient, "age_years"));
        addLabeledValue(patientParts, "Sexo", JsonSupport.string(patient, "sex"));
        addLabeledValue(patientParts, "ID Exame", JsonSupport.string(patient, "external_id"));
        if (!patientParts.isEmpty()) {
            sections.add("Paciente:\n" + String.join("\n", patientParts));
        }

        JsonNode assessment = JsonSupport.property(record, "assessment");
        List<String> headerLines = new ArrayList<>();
        addLabeledValue(headerLines, "Data do exame", JsonSupport.string(assessment, "performed_at"));
        addLabeledValue(headerLines, "ID exame", JsonSupport.string(assessment, "exam_id"));
        addLabeledValue(headerLines, "Protocolo",
                JsonSupport.string(JsonSupport.property(assessment, "protocol"), "description"));
        if (!headerLines.isEmpty()) {
            sections.add(String.join("\n", headerLines));
        }

        List<String> indexLines = new ArrayList<>();
        for (JsonNode index : JsonSupport.elements(JsonSupport.property(assessment, "posturographic_indices"))) {
            String line = buildEquilibrioIndexLine(index);
            if (line != null) {
                indexLines.add("- " + line);
            }
        }
        if (!indexLines.isEmpty()) {
            sections.add("Indices posturograficos:\n" + String.join("\n", indexLines));
        }

        List<String> rombergLines = new ArrayList<>();
        for (JsonNode quotient : JsonSupport.elements(JsonSupport.property(assessment, "romberg_quotients"))) {
            String line = buildRombergQuotientLine(quotient);
            if (line != null) {
                rombergLines.add("- " + line);
            }
        }
        if (!rombergLines.isEmpty()) {
            sections.add("Quocientes de Romberg:\n" + String.join("\n", rombergLines));
        }

        List<String> flagLines = buildEquilibrioFlagLines(assessment);
        if (!flagLines.isEmpty()) {
            sections.add("Sinalizadores:\n" + String.join("\n", flagLines));
        }
        String interpretation = JsonSupport.string(assessment, "interpretation");
        if (interpretation != null) {
            sections.add("Interpretacao:\n" + interpretation.trim());
        }
        List<String> notes = stringArray(assessment, "methodology_notes");
        if (!notes.isEmpty()) {
            sections.add("Notas metodologicas:\n" + String.join("\n", notes.stream().map("- "::concat).toList()));
        }
        return joinSections(sections);
    }

    private static String buildEquilibrioIndexLine(JsonNode index) {
        String label = defaultValue(JsonSupport.string(index, "label", "code"), "Indice");
        String value = doubleString(index, "value");
        if (value == null) {
            return null;
        }
        String unit = JsonSupport.string(index, "unit");
        String classification = JsonSupport.string(index, "classification");
        String line = label + ": " + value + (unit == null || unit.isBlank() ? "" : " " + unit);
        if (classification != null && !"not_classified".equalsIgnoreCase(classification)) {
            line += " (" + formatClassification(classification) + ")";
        }
        return line;
    }

    private static String buildRombergQuotientLine(JsonNode quotient) {
        String label = defaultValue(JsonSupport.string(quotient, "label", "code"), "Romberg");
        String value = doubleString(quotient, "value");
        if (value == null) {
            return null;
        }
        String classification = JsonSupport.string(quotient, "classification");
        return label + ": " + value
                + (classification == null || classification.isBlank() ? "" : " (" + formatClassification(classification) + ")");
    }

    private static List<String> buildEquilibrioFlagLines(JsonNode assessment) {
        List<String> lines = new ArrayList<>();
        JsonNode flags = JsonSupport.property(assessment, "automated_flags");
        String sway = JsonSupport.scalarString(JsonSupport.property(flags, "increased_postural_sway"));
        if (sway != null) {
            lines.add("- Oscilacao postural aumentada: " + sway);
        }
        JsonNode dependency = JsonSupport.property(flags, "visual_dependency");
        String status = JsonSupport.string(dependency, "status");
        String romberg = doubleString(dependency, "romberg_area_quotient");
        if (status != null && romberg != null) {
            lines.add("- Dependencia visual: " + status + " (Romberg area " + romberg + ")");
        } else if (status != null) {
            lines.add("- Dependencia visual: " + status);
        }
        if (Boolean.TRUE.equals(JsonSupport.booleanValue(flags, "lateral_predominance"))) {
            lines.add("- Predominio medio-lateral observado");
        }
        for (String warning : stringArray(flags, "acquisition_warnings")) {
            lines.add("- Aviso: " + warning);
        }
        return lines;
    }

    private static String formatClassification(String classification) {
        return switch (classification) {
            case "within_expected" -> "dentro do esperado";
            case "above_expected" -> "acima do esperado";
            case "below_expected" -> "abaixo do esperado";
            case "borderline" -> "faixa limitrofe";
            default -> classification.replace('_', ' ');
        };
    }

    private static List<String> buildCvTugFlagLines(JsonNode assessment) {
        List<String> lines = new ArrayList<>();
        JsonNode flags = JsonSupport.property(assessment, "automated_flags");
        if (has(flags, "tug_above_upper_limit")) {
            lines.add("- TUG acima do limite superior: "
                    + JsonSupport.scalarString(JsonSupport.property(flags, "tug_above_upper_limit")));
        }
        JsonNode fall = JsonSupport.property(flags, "fall_screening");
        addLabeledValue(lines, "- Triagem de quedas", JsonSupport.string(fall, "status"));

        JsonNode dtc = JsonSupport.property(flags, "dual_task_cost");
        String status = JsonSupport.string(dtc, "status");
        String percent = doubleString(dtc, "worst_percent");
        if (status != null && percent != null) {
            lines.add("- Dual-task cost: " + status + " (" + percent + "%)");
        } else {
            addLabeledValue(lines, "- Dual-task cost", status);
        }

        JsonNode gait = JsonSupport.property(flags, "gait_speed");
        addLabeledValue(lines, "- Velocidade media", doubleString(gait, "normal_condition_mps"), " m/s");
        addLabeledValue(lines, "- Nota velocidade", JsonSupport.string(gait, "note"));
        if (lines.isEmpty()) {
            JsonNode legacyFlags = JsonSupport.property(assessment, "flags");
            addLabeledValue(lines, "- Dual-task cost", JsonSupport.string(legacyFlags, "dual_task_cost_status"));
            addLabeledValue(lines, "- Velocidade media", doubleString(legacyFlags, "normal_walk_speed_mps"), " m/s");
            addLabeledValue(lines, "- Nota velocidade", JsonSupport.string(legacyFlags, "walk_speed_note"));
        }
        return lines;
    }

    private static List<JsonNode> extractRecordElements(JsonNode payload) {
        if (payload == null) {
            return new ArrayList<>();
        }
        if (payload.isArray()) {
            return JsonSupport.elements(payload);
        }
        if (!payload.isObject()) {
            return new ArrayList<>();
        }
        for (String key : RECORD_ARRAY_KEYS) {
            JsonNode candidate = JsonSupport.property(payload, key);
            if (candidate.isArray()) {
                return JsonSupport.elements(candidate);
            }
        }
        return new ArrayList<>();
    }

    private static String value(JsonNode node, String... keys) {
        return JsonSupport.string(node, keys);
    }

    private static String intString(JsonNode node, String key) {
        Integer value = JsonSupport.intValue(node, key);
        return value == null ? null : value.toString();
    }

    private static String doubleString(JsonNode node, String key) {
        Double value = JsonSupport.doubleValue(node, key);
        return value == null ? null : JsonSupport.formatDouble(value, "0.##");
    }

    private static Instant date(JsonNode record, Instant importedAt) {
        Instant parsed = JsonSupport.instant(record, DATE_KEYS);
        return parsed == null ? importedAt : parsed;
    }

    private static boolean has(JsonNode node, String key) {
        JsonNode value = JsonSupport.property(node, key);
        return value != null && !value.isNull() && !value.isMissingNode();
    }

    private static List<String> stringArray(JsonNode node, String key) {
        return JsonSupport.stringArray(node, key);
    }

    private static void addSection(List<String> sections, String value) {
        if (value != null && !value.isBlank()) {
            sections.add(value.trim());
        }
    }

    private static void addLabeledValue(List<String> lines, String label, String value) {
        addLabeledValue(lines, label, value, "");
    }

    private static void addLabeledValue(List<String> lines, String label, String value, String suffix) {
        if (value != null && !value.isBlank()) {
            lines.add(label + ": " + value + suffix);
        }
    }

    private static void addPhaseValue(List<String> parts, String label, String value) {
        if (value != null && !value.isBlank()) {
            parts.add(label + " " + value + "s");
        }
    }

    private static String joinSections(List<String> sections) {
        return String.join("\n\n", sections.stream().filter(section -> !section.isBlank()).toList());
    }

    private static String defaultString(String value) {
        return value == null ? "" : value;
    }

    private static String defaultValue(String value, String fallback) {
        return value == null || value.isBlank() ? fallback : value;
    }

    private static String createStableRecordId(String payloadId, String sourceId, int index) {
        return payloadId + ":" + sourceId + ":" + (index + 1);
    }
}
