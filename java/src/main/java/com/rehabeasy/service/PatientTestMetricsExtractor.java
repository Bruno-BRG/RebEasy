package com.rehabeasy.service;

import com.fasterxml.jackson.databind.JsonNode;
import com.rehabeasy.json.JsonSupport;
import com.rehabeasy.model.RehabEasyRecord;

import java.util.ArrayList;
import java.util.List;

public final class PatientTestMetricsExtractor {
    private PatientTestMetricsExtractor() {
    }

    public static String buildMetricsSummary(String rawPayloadJson, String testType) {
        return switch (testType) {
            case PatientRecordHelper.TEST_TYPE_CVTUG -> buildCvTugSummary(rawPayloadJson);
            case PatientRecordHelper.TEST_TYPE_EQUILIBRIO -> buildEquilibrioSummary(rawPayloadJson);
            case PatientRecordHelper.TEST_TYPE_INDEX_INDEX -> buildIndexIndexSummary(rawPayloadJson);
            default -> "Indicadores nao mapeados para este tipo de teste.";
        };
    }

    public static String buildDetailText(RehabEasyRecord record) {
        String testType = PatientRecordHelper.resolveTestType(record);
        StringBuilder builder = new StringBuilder();
        builder.append("Tipo de teste: ").append(testType).append('\n');
        builder.append("Titulo: ").append(record.title()).append('\n');
        builder.append("Origem: ").append(record.sender()).append('\n');
        builder.append("Recebido em: ").append(record.receivedAt()).append('\n');
        builder.append("Indicadores: ")
                .append(buildMetricsSummary(record.rawPayloadJson(), testType))
                .append('\n');
        if (!record.summary().isBlank()) {
            builder.append("Resumo: ").append(record.summary()).append('\n');
        }
        builder.append('\n');
        builder.append(record.plainTextContent().isBlank() ? record.rawPayloadJson() : record.plainTextContent());
        return builder.toString().stripTrailing();
    }

    private static String buildCvTugSummary(String rawPayloadJson) {
        List<String> parts = new ArrayList<>();
        try {
            JsonNode assessment = JsonSupport.property(JsonSupport.readTree(rawPayloadJson), "assessment");
            for (JsonNode condition : JsonSupport.elements(JsonSupport.property(assessment, "conditions"))) {
                String code = JsonSupport.string(condition, "code");
                Double seconds = JsonSupport.doubleValue(condition, "total_seconds");
                if (seconds != null) {
                    parts.add((code == null ? "condicao" : code) + " "
                            + JsonSupport.formatDouble(seconds, "0.0") + "s");
                }
            }
            JsonNode derived = JsonSupport.property(assessment, "derived_metrics");
            addDoubleMetric(parts, "DTC pior", JsonSupport.doubleValue(derived, "worst_dual_task_cost_percent"), "%");
            addDoubleMetric(parts, "Velocidade", JsonSupport.doubleValue(derived, "normal_walk_speed_mps"), " m/s");
            String status = JsonSupport.string(
                    JsonSupport.property(JsonSupport.property(assessment, "automated_flags"), "dual_task_cost"),
                    "status");
            if (status != null) {
                parts.add("Alerta DTC: " + status);
            }
        } catch (IllegalArgumentException exception) {
            return "Nao foi possivel extrair indicadores CvTUG.";
        }
        return parts.isEmpty() ? "Sem indicadores CvTUG disponiveis." : String.join(" | ", parts);
    }

    private static String buildEquilibrioSummary(String rawPayloadJson) {
        List<String> parts = new ArrayList<>();
        try {
            JsonNode assessment = JsonSupport.property(JsonSupport.readTree(rawPayloadJson), "assessment");
            JsonNode derived = JsonSupport.property(assessment, "derived_metrics");
            addDoubleMetric(parts, "SPL", JsonSupport.doubleValue(derived, "spl_mm"), " mm");
            addDoubleMetric(parts, "Velocidade osc.",
                    JsonSupport.doubleValue(derived, "mean_oscillation_velocity_mm_s"), " mm/s");
            addDoubleMetric(parts, "Romberg",
                    JsonSupport.doubleValue(derived, "romberg_area_quotient"), "");
            String status = JsonSupport.string(
                    JsonSupport.property(JsonSupport.property(assessment, "automated_flags"), "visual_dependency"),
                    "status");
            if (status != null) {
                parts.add("Dependencia visual: " + status);
            }
            String interpretation = JsonSupport.string(assessment, "interpretation");
            if (interpretation != null) {
                parts.add(interpretation);
            }
        } catch (IllegalArgumentException exception) {
            return "Nao foi possivel extrair indicadores de equilibrio.";
        }
        return parts.isEmpty() ? "Sem indicadores de equilibrio disponiveis." : String.join(" | ", parts);
    }

    private static String buildIndexIndexSummary(String rawPayloadJson) {
        List<String> parts = new ArrayList<>();
        try {
            JsonNode assessment = JsonSupport.property(JsonSupport.readTree(rawPayloadJson), "assessment");
            JsonNode derived = JsonSupport.property(assessment, "derived_metrics");
            addDoubleMetric(parts, "Distancia final",
                    JsonSupport.doubleValue(derived, "final_fingertip_distance_mm"), " mm");
            addDoubleMetric(parts, "Osc. geral",
                    JsonSupport.doubleValue(derived, "overall_oscillation_sd_mm"), " mm");
            addDoubleMetric(parts, "Assimetria",
                    JsonSupport.doubleValue(derived, "asymmetry_ratio"), "x");
            String status = JsonSupport.string(
                    JsonSupport.property(JsonSupport.property(assessment, "automated_flags"), "hand_asymmetry"),
                    "status");
            if (status != null) {
                parts.add("Assimetria: " + status);
            }
        } catch (IllegalArgumentException exception) {
            return "Nao foi possivel extrair indicadores Index-Index.";
        }
        return parts.isEmpty() ? "Sem indicadores Index-Index disponiveis." : String.join(" | ", parts);
    }

    private static void addDoubleMetric(List<String> parts, String label, Double value, String suffix) {
        if (value != null) {
            parts.add((label + " " + JsonSupport.formatDouble(value, "0.##") + suffix).strip());
        }
    }
}
