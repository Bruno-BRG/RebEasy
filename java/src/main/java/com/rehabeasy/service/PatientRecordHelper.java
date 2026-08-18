package com.rehabeasy.service;

import com.fasterxml.jackson.databind.JsonNode;
import com.rehabeasy.json.JsonSupport;
import com.rehabeasy.model.RehabEasyRecord;

public final class PatientRecordHelper {
    public static final String TEST_TYPE_CVTUG = "CvTUG";
    public static final String TEST_TYPE_EQUILIBRIO = "Equilibrio";
    public static final String TEST_TYPE_INDEX_INDEX = "Index-Index";
    public static final String TEST_TYPE_OTHER = "Outro";

    private PatientRecordHelper() {
    }

    public static String tryGetPatientExternalId(String rawPayloadJson) {
        return tryGetPatientString(rawPayloadJson, "external_id");
    }

    public static String tryGetPatientName(String rawPayloadJson) {
        return tryGetPatientString(rawPayloadJson, "name");
    }

    public static String resolveTestType(String rawPayloadJson) {
        if (rawPayloadJson == null || rawPayloadJson.isBlank()) {
            return TEST_TYPE_OTHER;
        }

        try {
            JsonNode root = JsonSupport.readTree(rawPayloadJson);
            String explicitType = firstNonBlank(
                    JsonSupport.string(root, "test_type"),
                    JsonSupport.string(JsonSupport.property(root, "assessment"), "test_type"),
                    JsonSupport.string(JsonSupport.property(root, "assessment"), "type"));
            if (explicitType != null) {
                return normalizeTestType(explicitType);
            }

            String sender = firstNonBlank(
                    JsonSupport.string(root, "sender"),
                    JsonSupport.string(root, "source"),
                    JsonSupport.string(root, "source_system"));
            if (equalsIgnoreCase(sender, TEST_TYPE_CVTUG)) {
                return TEST_TYPE_CVTUG;
            }
            if (equalsIgnoreCase(sender, "Posturografia VR")) {
                return TEST_TYPE_EQUILIBRIO;
            }
            if (equalsIgnoreCase(sender, TEST_TYPE_INDEX_INDEX) || equalsIgnoreCase(sender, "index-index")) {
                return TEST_TYPE_INDEX_INDEX;
            }

            JsonNode assessment = JsonSupport.property(root, "assessment");
            String assessmentTestType = JsonSupport.string(assessment, "test_type");
            if (assessmentTestType != null && assessmentTestType.toLowerCase().contains("index")) {
                return TEST_TYPE_INDEX_INDEX;
            }
            JsonNode metrics = JsonSupport.property(assessment, "metrics");
            if (!JsonSupport.property(metrics, "final_fingertip_distance_mm").isMissingNode()
                    && !JsonSupport.property(metrics, "final_fingertip_distance_mm").isNull()) {
                return TEST_TYPE_INDEX_INDEX;
            }
            if (!JsonSupport.property(assessment, "posturographic_indices").isMissingNode()
                    && !JsonSupport.property(assessment, "posturographic_indices").isNull()) {
                return TEST_TYPE_EQUILIBRIO;
            }
            if (!JsonSupport.property(assessment, "conditions").isMissingNode()
                    && !JsonSupport.property(assessment, "conditions").isNull()
                    && !JsonSupport.property(root, "patient").isMissingNode()
                    && !JsonSupport.property(root, "patient").isNull()) {
                return TEST_TYPE_CVTUG;
            }
            return TEST_TYPE_OTHER;
        } catch (IllegalArgumentException ignored) {
            return TEST_TYPE_OTHER;
        }
    }

    public static String resolvePatientId(RehabEasyRecord record) {
        if (record.patientId() != null && !record.patientId().isBlank()) {
            return record.patientId().trim();
        }
        String externalId = tryGetPatientExternalId(record.rawPayloadJson());
        return externalId == null ? "" : externalId.trim();
    }

    public static String resolveTestType(RehabEasyRecord record) {
        return record.testType() != null && !record.testType().isBlank()
                ? record.testType()
                : resolveTestType(record.rawPayloadJson());
    }

    private static String normalizeTestType(String testType) {
        String normalized = testType.toLowerCase();
        if (normalized.contains("tug")) {
            return TEST_TYPE_CVTUG;
        }
        if (normalized.contains("equil") || normalized.contains("posturo")) {
            return TEST_TYPE_EQUILIBRIO;
        }
        if (normalized.contains("index")) {
            return TEST_TYPE_INDEX_INDEX;
        }
        return testType.trim();
    }

    private static String tryGetPatientString(String rawPayloadJson, String propertyName) {
        if (rawPayloadJson == null || rawPayloadJson.isBlank()) {
            return null;
        }
        try {
            JsonNode patient = JsonSupport.property(JsonSupport.readTree(rawPayloadJson), "patient");
            return JsonSupport.string(patient, propertyName);
        } catch (IllegalArgumentException ignored) {
            return null;
        }
    }

    private static String firstNonBlank(String... values) {
        for (String value : values) {
            if (value != null && !value.isBlank()) {
                return value;
            }
        }
        return null;
    }

    private static boolean equalsIgnoreCase(String left, String right) {
        return left != null && left.equalsIgnoreCase(right);
    }
}
