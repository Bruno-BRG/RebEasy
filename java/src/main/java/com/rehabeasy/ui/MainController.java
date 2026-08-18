package com.rehabeasy.ui;

import com.rehabeasy.AppContext;
import com.rehabeasy.model.ApiPayloadImportResult;
import com.rehabeasy.model.PatientClinicalNote;
import com.rehabeasy.model.PatientClinicalNoteHistoryEntry;
import com.rehabeasy.model.PatientHistorySnapshot;
import com.rehabeasy.model.PatientTestHistoryEntry;
import com.rehabeasy.model.RehabEasyRecord;
import javafx.application.Platform;
import javafx.collections.FXCollections;
import javafx.event.ActionEvent;
import javafx.fxml.FXML;
import javafx.scene.Node;
import javafx.scene.chart.BarChart;
import javafx.scene.chart.CategoryAxis;
import javafx.scene.chart.NumberAxis;
import javafx.scene.chart.XYChart;
import javafx.scene.control.Alert;
import javafx.scene.control.Button;
import javafx.scene.control.ButtonType;
import javafx.scene.control.ComboBox;
import javafx.scene.control.Label;
import javafx.scene.control.ListCell;
import javafx.scene.control.ListView;
import javafx.scene.control.ProgressBar;
import javafx.scene.control.TextArea;
import javafx.scene.control.TextField;
import javafx.scene.input.Clipboard;
import javafx.scene.input.ClipboardContent;
import javafx.scene.paint.Color;
import javafx.util.Callback;

import java.time.Instant;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionException;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.function.Consumer;
import java.util.stream.Collectors;

public final class MainController {
    private static final String NOTE_PLACEHOLDER =
            "Escreva aqui a evolucao clinica, conduta e observacoes do paciente.";
    private static final DateTimeFormatter DATE_FORMAT =
            DateTimeFormatter.ofPattern("dd/MM/yyyy HH:mm").withZone(ZoneId.systemDefault());

    private final AppContext context;
    private final ExecutorService executor = Executors.newVirtualThreadPerTaskExecutor();
    private List<RehabEasyRecord> currentRecords = List.of();
    private PatientHistorySnapshot currentPatientHistory;
    private String lastHistoryReport = "";

    @FXML
    private TextField patientIdField;
    @FXML
    private Button refreshButton;
    @FXML
    private Label statusLabel;
    @FXML
    private ComboBox<String> dateSortComboBox;
    @FXML
    private ListView<RehabEasyRecord> recordsList;
    @FXML
    private Label historySummaryLabel;
    @FXML
    private ListView<PatientTimelineItem> timelineList;
    @FXML
    private Label recordTitleLabel;
    @FXML
    private Label recordMetaLabel;
    @FXML
    private TextArea recordBodyArea;
    @FXML
    private PdfPreviewPane pdfPreview;
    @FXML
    private Label summaryTitleLabel;
    @FXML
    private Label averageTimeLabel;
    @FXML
    private Label primaryMetricLabel;
    @FXML
    private Label testsCountLabel;
    @FXML
    private Label riskLabel;
    @FXML
    private ProgressBar clinicalProgressBar;
    @FXML
    private Label statusIndicatorLabel;
    @FXML
    private BarChart<String, Number> clinicalChart;
    @FXML
    private Label chartPanelTitleLabel;
    @FXML
    private Label chartLegendLabel;
    @FXML
    private Label chartNotesLabel;
    @FXML
    private TextArea clinicalNoteArea;
    @FXML
    private Label clinicalNoteStatusLabel;

    public MainController(AppContext context) {
        this.context = context;
    }

    @FXML
    private void initialize() {
        dateSortComboBox.setItems(FXCollections.observableArrayList(
                "Mais recente primeiro",
                "Mais antigo primeiro"));
        dateSortComboBox.getSelectionModel().selectFirst();
        dateSortComboBox.valueProperty().addListener((observable, oldValue, newValue) -> applyRecordSort());

        recordsList.setCellFactory(recordCellFactory());
        recordsList.getSelectionModel().selectedItemProperty()
                .addListener((observable, oldValue, newValue) -> showRecord(newValue));

        timelineList.setCellFactory(timelineCellFactory());
        timelineList.getSelectionModel().selectedItemProperty()
                .addListener((observable, oldValue, newValue) -> selectTimelineItem(newValue));

        patientIdField.focusedProperty().addListener((observable, oldValue, focused) -> {
            if (!focused) {
                loadPatientContext();
            }
        });
        resetClinicalNote();
        resetRecordDetails();
        updateCharts(null);
        loadLocalRecords();
    }

    @FXML
    private void onRefresh(ActionEvent ignored) {
        if (refreshButton.isDisabled()) {
            return;
        }
        patientIdField.clear();
        resetPatientContext();
        refreshButton.setDisable(true);
        refreshButton.setText("Atualizando...");
        statusLabel.setText("Buscando payloads pendentes na API...");

        CompletableFuture.supplyAsync(context.payloadImportService()::importAllPendingPayloads, executor)
                .thenAccept(results -> {
                    for (ApiPayloadImportResult result : results) {
                        context.recordStore().saveRecords(result.records());
                    }
                    Platform.runLater(() -> {
                        refreshButton.setDisable(false);
                        refreshButton.setText("Atualizar");
                        loadLocalRecords();
                        int recordCount = results.stream()
                                .mapToInt(result -> result.records().size())
                                .sum();
                        long pdfCount = results.stream()
                                .filter(result -> !result.pdfLocalPath().isBlank())
                                .count();
                        statusLabel.setText(results.isEmpty()
                                ? "Nenhum payload novo pendente."
                                : results.size() + " payload(s) importado(s), "
                                + recordCount + " registro(s), " + pdfCount + " PDF(s).");
                    });
                })
                .exceptionally(exception -> {
                    runOnUi(() -> {
                        refreshButton.setDisable(false);
                        refreshButton.setText("Atualizar");
                        statusLabel.setText("Falha ao atualizar pela API.");
                        showError("Erro na integracao com a API", unwrap(exception));
                    });
                    return null;
                });
    }

    @FXML
    private void onSaveClinicalNote(ActionEvent ignored) {
        String patientId = patientIdField.getText().trim();
        String content = getClinicalNoteContent();
        if (patientId.isBlank()) {
            showInformation("Prontuario", "Informe o ID do paciente antes de salvar o prontuario.");
            return;
        }
        if (content.isBlank()) {
            showInformation("Prontuario", "Escreva o conteudo do prontuario antes de salvar.");
            return;
        }

        setButtonsDisabled(true);
        CompletableFuture.runAsync(
                        () -> context.sqliteRecordStore().saveClinicalNote(patientId, content),
                        executor)
                .thenRun(() -> runOnUi(() -> {
                    setButtonsDisabled(false);
                    clinicalNoteStatusLabel.setText(
                            "Prontuario salvo para o paciente " + patientId + " em " + formatDate(Instant.now()) + ".");
                    statusLabel.setText(clinicalNoteStatusLabel.getText());
                    loadPatientHistory();
                }))
                .exceptionally(exception -> {
                    runOnUi(() -> {
                        setButtonsDisabled(false);
                        showError("Erro ao salvar prontuario", unwrap(exception));
                    });
                    return null;
                });
    }

    @FXML
    private void onCopyClinicalNote(ActionEvent ignored) {
        String patientId = patientIdField.getText().trim();
        if (patientId.isBlank()) {
            showInformation("Prontuario", "Informe o ID do paciente antes de copiar o prontuario.");
            return;
        }
        RehabEasyRecord selectedRecord = recordsList.getSelectionModel().getSelectedItem();
        String report = buildClinicalReportText(patientId, selectedRecord, getClinicalNoteContent());
        copyToClipboard(report);
        clinicalNoteStatusLabel.setText("Prontuario copiado para a area de transferencia.");
        statusLabel.setText(clinicalNoteStatusLabel.getText());
    }

    @FXML
    private void onInsertExamData(ActionEvent ignored) {
        RehabEasyRecord record = recordsList.getSelectionModel().getSelectedItem();
        if (record == null) {
            showInformation("Prontuario", "Selecione um registro de exame para inserir os dados.");
            return;
        }
        String current = getClinicalNoteContent();
        String section = buildExamDataSection(record);
        clinicalNoteArea.setText(current.isBlank() ? section : current + "\n\n" + section);
        clinicalNoteArea.positionCaret(clinicalNoteArea.getText().length());
        clinicalNoteArea.requestFocus();
    }

    @FXML
    private void onDeleteRecord(ActionEvent ignored) {
        RehabEasyRecord record = recordsList.getSelectionModel().getSelectedItem();
        if (record == null) {
            return;
        }
        Alert confirmation = new Alert(
                Alert.AlertType.CONFIRMATION,
                "Apagar o registro \"" + record.title() + "\" do RehabEasy local?",
                ButtonType.YES,
                ButtonType.NO);
        confirmation.setTitle("Apagar registro");
        confirmation.setHeaderText("Confirmar exclusao");
        confirmation.showAndWait()
                .filter(button -> button == ButtonType.YES)
                .ifPresent(button -> {
                    CompletableFuture.runAsync(
                                    () -> context.recordStore().deleteRecord(record.id()),
                                    executor)
                            .thenRun(() -> runOnUi(() -> {
                                resetRecordDetails();
                                loadLocalRecords();
                                statusLabel.setText("Registro apagado do RehabEasy local.");
                            }))
                            .exceptionally(exception -> {
                                runOnUi(() -> showError("Erro ao apagar registro", unwrap(exception)));
                                return null;
                            });
                });
    }

    @FXML
    private void onGenerateHistoryReport(ActionEvent ignored) {
        if (ensureHistoryReport()) {
            clinicalNoteStatusLabel.setText("Relatorio de historico gerado.");
            statusLabel.setText("Relatorio pronto. Use Copiar historico.");
        }
    }

    @FXML
    private void onCopyHistoryReport(ActionEvent ignored) {
        if (ensureHistoryReport()) {
            copyToClipboard(lastHistoryReport);
            clinicalNoteStatusLabel.setText("Historico completo copiado para a area de transferencia.");
            statusLabel.setText(clinicalNoteStatusLabel.getText());
        }
    }

    private Callback<ListView<RehabEasyRecord>, ListCell<RehabEasyRecord>> recordCellFactory() {
        return list -> new ListCell<>() {
            @Override
            protected void updateItem(RehabEasyRecord item, boolean empty) {
                super.updateItem(item, empty);
                setText(empty || item == null
                        ? null
                        : item.title() + "\n" + formatDate(item.receivedAt()));
                setWrapText(true);
            }
        };
    }

    private Callback<ListView<PatientTimelineItem>, ListCell<PatientTimelineItem>> timelineCellFactory() {
        return list -> new ListCell<>() {
            @Override
            protected void updateItem(PatientTimelineItem item, boolean empty) {
                super.updateItem(item, empty);
                setText(empty || item == null
                        ? null
                        : item.category() + " | " + item.headline() + "\n" + item.subline());
                setWrapText(true);
            }
        };
    }

    private void loadLocalRecords() {
        String query = patientIdField.getText().trim();
        CompletableFuture.supplyAsync(() -> context.recordStore().search(query.isBlank() ? null : query), executor)
                .thenAccept(records -> runOnUi(() -> {
                    currentRecords = List.copyOf(records);
                    applyRecordSort();
                    testsCountLabel.setText(Integer.toString(currentPatientHistory == null
                            ? currentRecords.size()
                            : currentPatientHistory.tests().size()));
                    if (records.isEmpty()) {
                        statusLabel.setText("Nenhum registro local encontrado.");
                    }
                }))
                .exceptionally(exception -> {
                    runOnUi(() -> showError("Erro ao carregar registros", unwrap(exception)));
                    return null;
                });
    }

    private void loadPatientContext() {
        String patientId = patientIdField.getText().trim();
        if (patientId.isBlank()) {
            resetPatientContext();
            loadLocalRecords();
            return;
        }
        loadClinicalNote();
        loadLocalRecords();
        loadPatientHistory();
    }

    private void loadClinicalNote() {
        String patientId = patientIdField.getText().trim();
        CompletableFuture.supplyAsync(
                        () -> context.sqliteRecordStore().getClinicalNote(patientId),
                        executor)
                .thenAccept(note -> runOnUi(() -> {
                    if (note == null || note.content().isBlank()) {
                        resetClinicalNote();
                        clinicalNoteStatusLabel.setText("Nenhum prontuario salvo para o paciente " + patientId + ".");
                    } else {
                        clinicalNoteArea.setText(note.content());
                        clinicalNoteStatusLabel.setText(
                                "Prontuario carregado. Ultima atualizacao: " + formatDate(note.updatedAt()) + ".");
                    }
                }))
                .exceptionally(exception -> {
                    runOnUi(() -> showError("Erro ao carregar prontuario", unwrap(exception)));
                    return null;
                });
    }

    private void loadPatientHistory() {
        String patientId = patientIdField.getText().trim();
        if (patientId.isBlank()) {
            return;
        }
        CompletableFuture.supplyAsync(
                        () -> context.patientHistoryService().getPatientHistory(patientId),
                        executor)
                .thenAccept(history -> runOnUi(() -> updateHistory(history)))
                .exceptionally(exception -> {
                    runOnUi(() -> showError("Erro ao carregar historico", unwrap(exception)));
                    return null;
                });
    }

    private void updateHistory(PatientHistorySnapshot history) {
        currentPatientHistory = history;
        lastHistoryReport = context.patientHistoryService().buildHistoryReport(history);
        Map<String, Long> counts = history.tests().stream()
                .collect(Collectors.groupingBy(PatientTestHistoryEntry::testType, Collectors.counting()));
        String testSummary = counts.isEmpty()
                ? "0 testes"
                : counts.entrySet().stream()
                .sorted(Map.Entry.comparingByKey())
                .map(entry -> entry.getValue() + " " + entry.getKey())
                .collect(Collectors.joining(", "));
        historySummaryLabel.setText(
                "Historico: " + testSummary + " | " + history.clinicalNotes().size() + " versoes de prontuario");

        List<PatientTimelineItem> timeline = new ArrayList<>();
        for (PatientTestHistoryEntry test : history.tests()) {
            timeline.add(new PatientTimelineItem(
                    "Teste",
                    test.testType() + " - " + test.title(),
                    formatDate(test.receivedAt()) + " | " + test.metricsSummary(),
                    test.receivedAt(),
                    test.recordId()));
        }
        for (PatientClinicalNoteHistoryEntry note : history.clinicalNotes()) {
            String preview = note.content().length() > 90
                    ? note.content().substring(0, 90) + "..."
                    : note.content();
            timeline.add(new PatientTimelineItem(
                    "Prontuario",
                    "Prontuario salvo",
                    formatDate(note.savedAt()) + " | " + preview,
                    note.savedAt(),
                    null));
        }
        timeline.sort(Comparator.comparing(PatientTimelineItem::occurredAt).reversed());
        timelineList.setItems(FXCollections.observableArrayList(timeline));
        testsCountLabel.setText(Integer.toString(history.tests().size()));
    }

    private void showRecord(RehabEasyRecord record) {
        if (record == null) {
            resetRecordDetails();
            return;
        }
        recordTitleLabel.setText(record.title());
        recordMetaLabel.setText(record.sender() + " -> " + record.recipient()
                + " | " + formatDate(record.receivedAt()));
        recordBodyArea.setText(record.plainTextContent().isBlank()
                ? record.rawPayloadJson()
                : record.plainTextContent());
        updateCharts(record);
        pdfPreview.load(record.pdfLocalPath());

        String patientExternalId = record.patientId().isBlank()
                ? com.rehabeasy.service.PatientRecordHelper.tryGetPatientExternalId(record.rawPayloadJson())
                : record.patientId();
        if (patientExternalId != null && !patientExternalId.isBlank()
                && !patientIdField.getText().trim().equalsIgnoreCase(patientExternalId)) {
            patientIdField.setText(patientExternalId);
            loadClinicalNote();
            loadPatientHistory();
        }
    }

    private void updateCharts(RehabEasyRecord record) {
        if (record == null) {
            clinicalChart.getData().clear();
            summaryTitleLabel.setText("Resumo");
            primaryMetricLabel.setText("Indicador principal");
            averageTimeLabel.setText("--");
            testsCountLabel.setText(Integer.toString(currentRecords.size()));
            riskLabel.setText("--");
            clinicalProgressBar.setProgress(0);
            statusIndicatorLabel.setText("--");
            chartPanelTitleLabel.setText("Graficos do exame");
            chartLegendLabel.setText("Selecione um exame para ver os graficos.");
            chartNotesLabel.setText("Aguardando registros importados da API.");
            return;
        }

        ClinicalMetricsService.ClinicalMetrics metrics = ClinicalMetricsService.analyze(record);
        summaryTitleLabel.setText(metrics.summaryTitle());
        primaryMetricLabel.setText(metrics.primaryLabel());
        averageTimeLabel.setText(metrics.primaryValue());
        riskLabel.setText(metrics.risk());
        clinicalProgressBar.setMaxWidth(Double.MAX_VALUE);
        clinicalProgressBar.setProgress(metrics.progressMaximum() <= 0
                ? 0
                : metrics.progressValue() / metrics.progressMaximum());
        statusIndicatorLabel.setText(metrics.alert() ? "Alerta" : "OK");
        statusIndicatorLabel.setTextFill(metrics.alert() ? Color.web("#C7772E") : Color.web("#0E6B7A"));
        chartPanelTitleLabel.setText(
                switch (metrics.testType()) {
                    case "CvTUG" -> "Tempos e custo dual-task";
                    case "Equilibrio" -> "Indices posturograficos";
                    case "Index-Index" -> "Oscilacao e toque";
                    default -> "Graficos do exame";
                });
        chartLegendLabel.setText(metrics.legend());
        chartNotesLabel.setText(metrics.note());
        renderChart(metrics.bars());
    }

    private void renderChart(List<ClinicalMetricsService.ChartBar> bars) {
        clinicalChart.getData().clear();
        XYChart.Series<String, Number> series = new XYChart.Series<>();
        for (ClinicalMetricsService.ChartBar bar : bars) {
            XYChart.Data<String, Number> data = new XYChart.Data<>(
                    bar.label(),
                    bar.value() == null ? 0 : bar.value());
            series.getData().add(data);
        }
        clinicalChart.getData().add(series);
        for (int index = 0; index < bars.size(); index++) {
            Node node = series.getData().get(index).getNode();
            if (node != null && bars.get(index).warning()) {
                node.setStyle("-fx-bar-fill: #C7772E;");
            }
        }
    }

    private void selectTimelineItem(PatientTimelineItem item) {
        if (item == null || item.recordId() == null) {
            return;
        }
        currentRecords.stream()
                .filter(record -> record.id().equals(item.recordId()))
                .findFirst()
                .ifPresent(record -> recordsList.getSelectionModel().select(record));
    }

    private void applyRecordSort() {
        boolean ascending = dateSortComboBox.getSelectionModel().getSelectedIndex() == 1;
        List<RehabEasyRecord> sorted = currentRecords.stream()
                .sorted(ascending
                        ? Comparator.comparing(RehabEasyRecord::receivedAt)
                        : Comparator.comparing(RehabEasyRecord::receivedAt).reversed())
                .toList();
        RehabEasyRecord selected = recordsList.getSelectionModel().getSelectedItem();
        recordsList.setItems(FXCollections.observableArrayList(sorted));
        if (selected != null) {
            sorted.stream()
                    .filter(record -> record.id().equals(selected.id()))
                    .findFirst()
                    .ifPresent(record -> recordsList.getSelectionModel().select(record));
        } else if (!sorted.isEmpty()) {
            recordsList.getSelectionModel().selectFirst();
        }
    }

    private void resetRecordDetails() {
        recordTitleLabel.setText("Nenhum registro selecionado");
        recordMetaLabel.setText("Selecione um registro na lista para ver o exame, PDF e graficos.");
        recordBodyArea.setText("Os dados do exame aparecem aqui.");
        pdfPreview.clear();
        updateCharts(null);
        recordsList.getSelectionModel().clearSelection();
    }

    private void resetPatientContext() {
        currentPatientHistory = null;
        lastHistoryReport = "";
        historySummaryLabel.setText("Informe o ID do paciente para ver o historico.");
        timelineList.getItems().clear();
        resetClinicalNote();
    }

    private void resetClinicalNote() {
        clinicalNoteArea.setText("");
        clinicalNoteArea.setPromptText(NOTE_PLACEHOLDER);
        clinicalNoteStatusLabel.setText("Informe o ID do paciente para salvar o prontuario.");
    }

    private String getClinicalNoteContent() {
        return clinicalNoteArea.getText() == null ? "" : clinicalNoteArea.getText().trim();
    }

    private boolean ensureHistoryReport() {
        String patientId = patientIdField.getText().trim();
        if (patientId.isBlank()) {
            showInformation("Historico do paciente", "Informe o ID do paciente para gerar o relatorio.");
            return false;
        }
        if (currentPatientHistory == null) {
            currentPatientHistory = context.patientHistoryService().getPatientHistory(patientId);
            lastHistoryReport = context.patientHistoryService().buildHistoryReport(currentPatientHistory);
        }
        if (currentPatientHistory.tests().isEmpty() && currentPatientHistory.clinicalNotes().isEmpty()) {
            showInformation("Historico do paciente", "Nenhum historico encontrado para este paciente.");
            return false;
        }
        return true;
    }

    private String buildExamDataSection(RehabEasyRecord record) {
        String patientName = com.rehabeasy.service.PatientRecordHelper.tryGetPatientName(record.rawPayloadJson());
        StringBuilder builder = new StringBuilder("--- DADOS DO EXAME ---\n");
        builder.append("Exame: ").append(record.title()).append('\n');
        builder.append("Origem: ").append(record.sender()).append('\n');
        builder.append("Recebido em: ").append(formatDate(record.receivedAt())).append('\n');
        if (patientName != null && !patientName.isBlank()) {
            builder.append("Paciente: ").append(patientName).append('\n');
        }
        if (!record.summary().isBlank()) {
            builder.append("Resumo: ").append(record.summary()).append('\n');
        }
        builder.append('\n').append(record.plainTextContent().isBlank()
                ? record.rawPayloadJson()
                : record.plainTextContent());
        return builder.toString().stripTrailing();
    }

    private String buildClinicalReportText(
            String patientId,
            RehabEasyRecord selectedRecord,
            String clinicalNote
    ) {
        StringBuilder builder = new StringBuilder("EVOLUCAO CLINICA - RehabEasy\n");
        builder.append("Paciente ID: ").append(patientId).append('\n');
        builder.append("Gerado em: ").append(formatDate(Instant.now())).append("\n\n");
        if (selectedRecord != null) {
            builder.append(buildExamDataSection(selectedRecord)).append("\n\n");
            builder.append("--- INDICADORES ---\n");
            ClinicalMetricsService.ClinicalMetrics metrics = ClinicalMetricsService.analyze(selectedRecord);
            builder.append(metrics.summaryTitle()).append(": ").append(metrics.primaryValue())
                    .append(" (").append(metrics.primaryLabel()).append(")\n");
            builder.append("Alerta: ").append(metrics.risk()).append('\n');
            builder.append(metrics.bars().stream()
                    .map(bar -> bar.label() + ": " + bar.valueLabel())
                    .collect(Collectors.joining("\n"))).append("\n\n");
        }
        builder.append("--- PRONTUARIO / EVOLUCAO ---\n");
        builder.append(clinicalNote.isBlank() ? "(Sem texto de prontuario registrado.)" : clinicalNote);
        return builder.toString().stripTrailing();
    }

    private void copyToClipboard(String value) {
        ClipboardContent content = new ClipboardContent();
        content.putString(value);
        Clipboard.getSystemClipboard().setContent(content);
    }

    private void setButtonsDisabled(boolean disabled) {
        refreshButton.setDisable(disabled);
    }

    private void runOnUi(Runnable action) {
        if (Platform.isFxApplicationThread()) {
            action.run();
        } else {
            Platform.runLater(action);
        }
    }

    private static Throwable unwrap(Throwable exception) {
        Throwable current = exception;
        while ((current instanceof CompletionException) && current.getCause() != null) {
            current = current.getCause();
        }
        return current;
    }

    private static String formatDate(Instant instant) {
        return DATE_FORMAT.format(instant);
    }

    private void showInformation(String title, String message) {
        new Alert(Alert.AlertType.INFORMATION, message, ButtonType.OK) {{
            setTitle(title);
            setHeaderText(null);
        }}.showAndWait();
    }

    private void showError(String title, Throwable exception) {
        String message = exception == null || exception.getMessage() == null
                ? "Erro inesperado."
                : exception.getMessage();
        new Alert(Alert.AlertType.ERROR, message, ButtonType.OK) {{
            setTitle(title);
            setHeaderText(null);
        }}.showAndWait();
    }

    public void shutdown() {
        executor.shutdownNow();
        pdfPreview.shutdown();
    }
}
