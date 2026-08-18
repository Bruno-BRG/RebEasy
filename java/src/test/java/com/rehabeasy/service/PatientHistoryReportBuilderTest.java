package com.rehabeasy.service;

import com.rehabeasy.model.PatientClinicalNote;
import com.rehabeasy.model.PatientClinicalNoteHistoryEntry;
import com.rehabeasy.model.PatientHistorySnapshot;
import com.rehabeasy.model.PatientTestHistoryEntry;
import org.junit.jupiter.api.Test;

import java.time.Instant;
import java.util.List;

import static org.junit.jupiter.api.Assertions.assertTrue;

class PatientHistoryReportBuilderTest {
    @Test
    void buildsClinicalHistoryWithTestsAndNotes() {
        Instant now = Instant.parse("2026-08-17T12:00:00Z");
        PatientHistorySnapshot history = new PatientHistorySnapshot(
                "patient-1",
                "Paciente Teste",
                List.of(new PatientTestHistoryEntry(
                        "record-1",
                        "CvTUG",
                        "Exame de marcha",
                        now,
                        "normal 10.4s",
                        "Detalhes do exame")),
                List.of(new PatientClinicalNoteHistoryEntry(
                        1,
                        "patient-1",
                        "Conduta clinica.",
                        now)),
                new PatientClinicalNote("patient-1", "Prontuario atual.", now));

        String report = PatientHistoryReportBuilder.build(history);

        assertTrue(report.contains("Paciente ID: patient-1"));
        assertTrue(report.contains("Total de testes: 1"));
        assertTrue(report.contains("Exame de marcha"));
        assertTrue(report.contains("Conduta clinica."));
        assertTrue(report.contains("Prontuario atual."));
    }
}
