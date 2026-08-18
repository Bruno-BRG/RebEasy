package com.rehabeasy.service;

import com.rehabeasy.model.PatientClinicalNote;
import com.rehabeasy.model.PatientClinicalNoteHistoryEntry;
import com.rehabeasy.model.PatientHistorySnapshot;
import com.rehabeasy.model.PatientTestHistoryEntry;

import java.time.ZoneId;
import java.time.format.DateTimeFormatter;

public final class PatientHistoryReportBuilder {
    private static final DateTimeFormatter DATE_FORMAT =
            DateTimeFormatter.ofPattern("dd/MM/yyyy HH:mm").withZone(ZoneId.systemDefault());

    private PatientHistoryReportBuilder() {
    }

    public static String build(PatientHistorySnapshot history) {
        StringBuilder builder = new StringBuilder();
        builder.append("RELATORIO DE HISTORICO DO PACIENTE - RehabEasy\n");
        builder.append("Paciente ID: ").append(history.patientId()).append('\n');
        if (history.patientName() != null && !history.patientName().isBlank()) {
            builder.append("Nome: ").append(history.patientName()).append('\n');
        }
        builder.append("Gerado em: ").append(DATE_FORMAT.format(java.time.Instant.now())).append('\n');
        builder.append("Total de testes: ").append(history.tests().size()).append('\n');
        builder.append("Versoes de prontuario: ").append(history.clinicalNotes().size()).append("\n\n");

        builder.append("=== HISTORICO DE TESTES ===\n");
        if (history.tests().isEmpty()) {
            builder.append("Nenhum teste registrado para este paciente.\n");
        } else {
            int index = 1;
            for (PatientTestHistoryEntry test : history.tests()) {
                builder.append('\n');
                builder.append('[').append(index++).append("] ")
                        .append(DATE_FORMAT.format(test.receivedAt()))
                        .append(" | ").append(test.testType())
                        .append(" | ").append(test.title()).append('\n');
                builder.append("Indicadores: ").append(test.metricsSummary()).append('\n');
                builder.append(test.detailText()).append('\n');
                builder.append("-".repeat(72)).append('\n');
            }
        }

        builder.append("\n=== HISTORICO DE PRONTUARIO ===\n");
        if (history.clinicalNotes().isEmpty()) {
            builder.append("Nenhuma versao anterior de prontuario salva.\n");
        } else {
            int index = 1;
            for (PatientClinicalNoteHistoryEntry note : history.clinicalNotes()) {
                builder.append('\n');
                builder.append('[').append(index++).append("] Salvo em ")
                        .append(DATE_FORMAT.format(note.savedAt())).append('\n');
                builder.append(note.content()).append('\n');
                builder.append("-".repeat(72)).append('\n');
            }
        }

        builder.append("\n=== PRONTUARIO ATUAL ===\n");
        PatientClinicalNote current = history.currentClinicalNote();
        if (current == null || current.content().isBlank()) {
            builder.append("(Sem prontuario atual registrado.)");
        } else {
            builder.append("Ultima atualizacao: ")
                    .append(DATE_FORMAT.format(current.updatedAt())).append('\n');
            builder.append(current.content());
        }
        return builder.toString().stripTrailing();
    }
}
