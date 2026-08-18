package com.rehabeasy.ui;

import com.fasterxml.jackson.databind.JsonNode;
import com.rehabeasy.json.JsonSupport;
import com.rehabeasy.model.RehabEasyRecord;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

public final class ClinicalMetricsService {
    private ClinicalMetricsService() {
    }

    public static ClinicalMetrics analyze(RehabEasyRecord record) {
        JsonNode root;
        try {
            root = JsonSupport.readTree(record.rawPayloadJson());
        } catch (IllegalArgumentException exception) {
            return ClinicalMetrics.empty(record.testType());
        }
        JsonNode assessment = JsonSupport.property(root, "assessment");
        if (looksLikeIndexIndex(assessment)) {
            return indexIndex(assessment);
        }
        if (looksLikeEquilibrio(assessment)) {
            return equilibrio(assessment);
        }
        return cvTug(assessment);
    }

    private static ClinicalMetrics indexIndex(JsonNode assessment) {
        JsonNode derived = JsonSupport.property(assessment, "derived_metrics");
        JsonNode raw = JsonSupport.property(assessment, "metrics");
        Double finalDistance = firstDouble(derived, raw, "final_fingertip_distance_mm");
        Double left = firstDouble(derived, raw, "left_hand_oscillation_sd_mm");
        Double right = firstDouble(derived, raw, "right_hand_oscillation_sd_mm");
        Double overall = firstDouble(derived, raw, "overall_oscillation_sd_mm");
        Double threshold = firstDouble(raw, JsonSupport.property(assessment, "protocol"), "touch_threshold_mm");

        JsonNode flags = JsonSupport.property(assessment, "automated_flags");
        JsonNode asymmetry = JsonSupport.property(flags, "hand_asymmetry");
        String asymmetryStatus = JsonSupport.string(asymmetry, "status");
        boolean alert = containsAlert(asymmetryStatus)
                || Boolean.FALSE.equals(JsonSupport.booleanValue(flags, "touch_within_threshold"));
        String interpretation = JsonSupport.string(assessment, "interpretation");

        double max = Math.max(threshold == null ? 15 : threshold, 15);
        return new ClinicalMetrics(
                "Index-Index",
                "Resumo Index-Index",
                "Distancia final (mm)",
                format(finalDistance, "0.#", "mm"),
                max,
                clamp(finalDistance, 0, max),
                defaultValue(asymmetryStatus, "--"),
                alert,
                defaultValue(interpretation, "Indicadores extraidos do relatorio Index-Index selecionado."),
                "Barras: oscilacao (DP) por mao e geral. Distancia final vs limiar de toque.",
                bars(
                        bar("Esq.", left, "0.#", "Oscilacao mao esquerda (DP)", false),
                        bar("Dir.", right, "0.#", "Oscilacao mao direita (DP)", true),
                        bar("Geral", overall, "0.#", "Oscilacao geral (DP)", false),
                        bar("Dist.", finalDistance, "0.#", "Distancia final entre pontas (mm)", false),
                        bar("Limiar", threshold, "0.#", "Limiar de toque configurado (mm)", false)
                ));
    }

    private static ClinicalMetrics equilibrio(JsonNode assessment) {
        JsonNode derived = JsonSupport.property(assessment, "derived_metrics");
        Double spl = JsonSupport.doubleValue(derived, "spl_mm");
        Double area = JsonSupport.doubleValue(derived, "confidence_ellipse_95_area_mm2");
        Double velocity = JsonSupport.doubleValue(derived, "mean_oscillation_velocity_mm_s");
        Double romberg = JsonSupport.doubleValue(derived, "romberg_area_quotient");
        JsonNode flags = JsonSupport.property(assessment, "automated_flags");
        JsonNode dependency = JsonSupport.property(flags, "visual_dependency");
        String dependencyStatus = JsonSupport.string(dependency, "status");
        if (romberg == null) {
            romberg = JsonSupport.doubleValue(dependency, "romberg_area_quotient");
        }
        boolean alert = containsAlert(dependencyStatus)
                || Boolean.TRUE.equals(JsonSupport.booleanValue(flags, "increased_postural_sway"));
        return new ClinicalMetrics(
                "Equilibrio",
                "Resumo Equilibrio",
                "SPL (mm)",
                format(spl, "0.#", "mm"),
                500,
                clamp(spl, 0, 500),
                defaultValue(dependencyStatus, "--"),
                alert,
                defaultValue(JsonSupport.string(assessment, "interpretation"),
                        "Indicadores extraidos do relatorio de equilibrio selecionado."),
                "Barras: SPL, area da elipse, velocidade e Romberg (limite tipico 2.0).",
                bars(
                        bar("SPL", spl, "0", "Comprimento de trajetoria (mm)", false),
                        bar("Area", area, "0", "Area da elipse 95% (mm2)", false),
                        bar("Vel.", velocity, "0.##", "Velocidade media (mm/s)", false),
                        bar("Romberg", romberg, "0.##", "Quociente de Romberg area (limite ~2.0)",
                                romberg != null && romberg >= 2.0)
                ));
    }

    private static ClinicalMetrics cvTug(JsonNode assessment) {
        Double normal = null;
        Double motor = null;
        Double cognitive = null;
        for (JsonNode condition : JsonSupport.elements(JsonSupport.property(assessment, "conditions"))) {
            String code = JsonSupport.string(condition, "code");
            Double total = JsonSupport.doubleValue(condition, "total_seconds");
            if ("normal".equalsIgnoreCase(code)) {
                normal = total;
            } else if ("motor".equalsIgnoreCase(code)) {
                motor = total;
            } else if ("cognitive".equalsIgnoreCase(code)) {
                cognitive = total;
            }
        }
        JsonNode derived = JsonSupport.property(assessment, "derived_metrics");
        Double worstDtc = JsonSupport.doubleValue(derived, "worst_dual_task_cost_percent");
        JsonNode flags = JsonSupport.property(assessment, "automated_flags");
        JsonNode dtc = JsonSupport.property(flags, "dual_task_cost");
        if (worstDtc == null) {
            worstDtc = JsonSupport.doubleValue(dtc, "worst_percent");
        }
        String status = JsonSupport.string(dtc, "status");
        String speedNote = JsonSupport.string(JsonSupport.property(flags, "gait_speed"), "note");
        boolean alert = containsAlert(status);
        return new ClinicalMetrics(
                "CvTUG",
                "Resumo TUG",
                "Tempo normal",
                format(normal, "0.0", "s"),
                20,
                clamp(normal, 0, 20),
                defaultValue(status, "--"),
                alert,
                defaultValue(speedNote, "Indicadores extraidos do payload selecionado."),
                "Barras: tempos TUG (Normal/Motora/Cognitiva) e DTC pior em %.",
                bars(
                        bar("Normal", normal, "0.#", "Tempo total na condicao Normal", false),
                        bar("Motora", motor, "0.#", "Tempo total na condicao Motora", false),
                        bar("Cognitiva", cognitive, "0.#", "Tempo total na condicao Cognitiva", false),
                        bar("DTC", worstDtc, "0.#", "Pior dual-task cost entre as condicoes", true)
                ));
    }

    private static boolean looksLikeIndexIndex(JsonNode assessment) {
        JsonNode metrics = JsonSupport.property(assessment, "metrics");
        return JsonSupport.doubleValue(metrics, "final_fingertip_distance_mm") != null
                || "INDEX_INDEX".equalsIgnoreCase(JsonSupport.string(assessment, "test_type"));
    }

    private static boolean looksLikeEquilibrio(JsonNode assessment) {
        return JsonSupport.property(assessment, "posturographic_indices").isArray();
    }

    private static Double firstDouble(JsonNode first, JsonNode second, String name) {
        Double value = JsonSupport.doubleValue(first, name);
        return value == null ? JsonSupport.doubleValue(second, name) : value;
    }

    private static List<ChartBar> bars(ChartBar... bars) {
        return List.of(bars);
    }

    private static ChartBar bar(String label, Double value, String format, String tooltip, boolean warning) {
        return new ChartBar(label, value, format(value, format, ""), tooltip, warning);
    }

    private static String format(Double value, String pattern, String suffix) {
        return value == null ? "--" : JsonSupport.formatDouble(value, pattern) + suffix;
    }

    private static boolean containsAlert(String value) {
        return value != null && value.toUpperCase(Locale.ROOT).contains("ALERTA");
    }

    private static String defaultValue(String value, String fallback) {
        return value == null || value.isBlank() ? fallback : value;
    }

    private static double clamp(Double value, double min, double max) {
        if (value == null) {
            return 0;
        }
        return Math.max(min, Math.min(max, value));
    }

    public record ChartBar(
            String label,
            Double value,
            String valueLabel,
            String tooltip,
            boolean warning
    ) {
    }

    public record ClinicalMetrics(
            String testType,
            String summaryTitle,
            String primaryLabel,
            String primaryValue,
            double progressMaximum,
            double progressValue,
            String risk,
            boolean alert,
            String note,
            String legend,
            List<ChartBar> bars
    ) {
        public ClinicalMetrics {
            bars = bars == null ? List.of() : List.copyOf(bars);
        }

        public static ClinicalMetrics empty(String testType) {
            return new ClinicalMetrics(
                    testType == null ? "Outro" : testType,
                    "Resumo",
                    "Indicador principal",
                    "--",
                    20,
                    0,
                    "--",
                    false,
                    "Aguardando registros importados da API.",
                    "Selecione um exame para ver os graficos.",
                    List.of());
        }
    }
}
