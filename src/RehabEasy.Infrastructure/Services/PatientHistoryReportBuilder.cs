using System.Text;
using RehabEasy.Domain.Models;

namespace RehabEasy.Infrastructure.Services;

public static class PatientHistoryReportBuilder
{
    public static string Build(PatientHistorySnapshot history)
    {
        StringBuilder builder = new();
        builder.AppendLine("RELATORIO DE HISTORICO DO PACIENTE - RehabEasy");
        builder.AppendLine($"Paciente ID: {history.PatientId}");

        if (!string.IsNullOrWhiteSpace(history.PatientName))
        {
            builder.AppendLine($"Nome: {history.PatientName}");
        }

        builder.AppendLine($"Gerado em: {DateTime.Now:g}");
        builder.AppendLine($"Total de testes: {history.Tests.Count}");
        builder.AppendLine($"Versoes de prontuario: {history.ClinicalNotes.Count}");
        builder.AppendLine();

        builder.AppendLine("=== HISTORICO DE TESTES ===");
        if (history.Tests.Count == 0)
        {
            builder.AppendLine("Nenhum teste registrado para este paciente.");
        }
        else
        {
            int index = 1;
            foreach (PatientTestHistoryEntry test in history.Tests.OrderByDescending(test => test.ReceivedAt))
            {
                builder.AppendLine();
                builder.AppendLine($"[{index}] {test.ReceivedAt.LocalDateTime:g} | {test.TestType} | {test.Title}");
                builder.AppendLine($"Indicadores: {test.MetricsSummary}");
                builder.AppendLine(test.DetailText);
                builder.AppendLine(new string('-', 72));
                index++;
            }
        }

        builder.AppendLine();
        builder.AppendLine("=== HISTORICO DE PRONTUARIO ===");
        if (history.ClinicalNotes.Count == 0)
        {
            builder.AppendLine("Nenhuma versao anterior de prontuario salva.");
        }
        else
        {
            int index = 1;
            foreach (PatientClinicalNoteHistoryEntry note in history.ClinicalNotes.OrderByDescending(note => note.SavedAt))
            {
                builder.AppendLine();
                builder.AppendLine($"[{index}] Salvo em {note.SavedAt.LocalDateTime:g}");
                builder.AppendLine(note.Content);
                builder.AppendLine(new string('-', 72));
                index++;
            }
        }

        builder.AppendLine();
        builder.AppendLine("=== PRONTUARIO ATUAL ===");
        if (history.CurrentClinicalNote is null || string.IsNullOrWhiteSpace(history.CurrentClinicalNote.Content))
        {
            builder.AppendLine("(Sem prontuario atual registrado.)");
        }
        else
        {
            builder.AppendLine(
                $"Ultima atualizacao: {history.CurrentClinicalNote.UpdatedAt.LocalDateTime:g}");
            builder.AppendLine(history.CurrentClinicalNote.Content);
        }

        return builder.ToString().TrimEnd();
    }
}
