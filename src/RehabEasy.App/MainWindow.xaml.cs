using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RehabEasy.Domain.Contracts;
using RehabEasy.Domain.Models;
using RehabEasy.Infrastructure.Services;

namespace RehabEasy.App;

public partial class MainWindow : Window
{
    private const string ApiBaseUrlEnv = "REHABEASY_API_BASE_URL";
    private const string SystemBApiKeyEnv = "REHABEASY_SYSTEM_B_API_KEY";
    private const string DefaultApiBaseUrl = "https://telemedicinacc.vercel.app";
    private const string DefaultSystemBApiKey = "rehabeasy-system-b";

    private readonly IApiPayloadImportService? _payloadImportService;
    private readonly IRecordStore _recordStore;
    private readonly IClinicalNoteStore _clinicalNoteStore;
    private readonly IPatientHistoryService _patientHistoryService;
    private List<RehabEasyRecord> _currentRecords = [];
    private PatientHistorySnapshot? _currentPatientHistory;
    private string _lastHistoryReport = string.Empty;
    private bool _isLoadingClinicalNote;
    private const string ClinicalNotePlaceholder =
        "Escreva aqui a evolucao clinica, conduta e observacoes do paciente.";

    public MainWindow()
    {
        InitializeComponent();

        SqliteRecordStore sqliteRecordStore = new(GetDatabasePath());
        sqliteRecordStore.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        _recordStore = sqliteRecordStore;
        _clinicalNoteStore = sqliteRecordStore;
        _patientHistoryService = new PatientHistoryService(_recordStore, _clinicalNoteStore);

        DataObject.AddPastingHandler(PatientIdTextBox, PatientIdTextBox_OnPaste);
        LoadLocalRecordsAsync().GetAwaiter().GetResult();
        LoadPatientHistoryAsync().GetAwaiter().GetResult();

        if (!TryCreateApiPayloadImportService(out IApiPayloadImportService? importService, out string configurationMessage))
        {
            ApplyMissingConfigurationState(configurationMessage);
            return;
        }

        _payloadImportService = importService;
    }

    private void PatientActionsMenuButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (PatientActionsMenuButton.ContextMenu is ContextMenu menu)
        {
            menu.PlacementTarget = PatientActionsMenuButton;
            menu.IsOpen = true;
        }
    }

    private void PatientActionsContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        DeleteMenuItem.IsEnabled = MessagesList.SelectedItem is RehabEasyRecord;
        DeleteButton.IsEnabled = DeleteMenuItem.IsEnabled;
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshFromApiAsync();
    }

    private void MessagesList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MessagesList.SelectedItem is not RehabEasyRecord record)
        {
            return;
        }

        SubjectText.Text = record.Title;
        MetaText.Text = $"{record.Sender} -> {record.Recipient} | {record.ReceivedAt.LocalDateTime:g}";
        BodyText.Text = string.IsNullOrWhiteSpace(record.PlainTextContent)
            ? record.RawPayloadJson
            : record.PlainTextContent;
        DeleteButton.IsEnabled = true;
        UpdateCharts(record);
        _ = ShowPdfAsync(record.PdfLocalPath);
        SyncPatientIdFromRecord(record);
    }

    private async void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (MessagesList.SelectedItem is not RehabEasyRecord record)
        {
            DeleteButton.IsEnabled = false;
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            $"Apagar o registro \"{record.Title}\" do RehabEasy local?",
            "Apagar registro",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        DeleteButton.IsEnabled = false;

        try
        {
            await _recordStore.DeleteRecordAsync(record.Id, CancellationToken.None);
            await LoadLocalRecordsAsync();
            SubjectText.Text = "Nenhum registro selecionado";
            MetaText.Text = "Selecione um registro na aba Registros.";
            BodyText.Text = "Os dados do exame aparecem aqui.";
            await ClearPdfAsync();
            StatusText.Text = "Registro apagado do RehabEasy local.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Falha ao apagar registro.";
            MessageBox.Show(
                this,
                exception.Message,
                "Erro ao apagar registro",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void DateSortComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        ApplyRecordSort();
    }

    private void PatientIdTextBox_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !IsAlphanumeric(e.Text);
    }

    private void PatientIdTextBox_OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        string pastedText = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;

        if (!IsAlphanumeric(pastedText))
        {
            e.CancelCommand();
        }
    }

    private void ClinicalNoteTextBox_OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (string.Equals(ClinicalNoteTextBox.Text, ClinicalNotePlaceholder, StringComparison.Ordinal))
        {
            ClinicalNoteTextBox.Text = string.Empty;
        }
    }

    private async void PatientIdTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        await LoadClinicalNoteForCurrentPatientAsync();
        await LoadLocalRecordsAsync();
        await LoadPatientHistoryAsync();
    }

    private async void SaveClinicalNoteButton_OnClick(object sender, RoutedEventArgs e)
    {
        string patientId = PatientIdTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(patientId))
        {
            MessageBox.Show(
                this,
                "Informe o ID do paciente antes de salvar o prontuario.",
                "Prontuario",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string content = GetClinicalNoteContent();
        if (string.IsNullOrWhiteSpace(content))
        {
            MessageBox.Show(
                this,
                "Escreva o conteudo do prontuario antes de salvar.",
                "Prontuario",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            await _clinicalNoteStore.SaveClinicalNoteAsync(patientId, content, CancellationToken.None);
            ClinicalNoteStatusText.Text = $"Prontuario salvo para o paciente {patientId} em {DateTime.Now:g}.";
            StatusText.Text = ClinicalNoteStatusText.Text;
            await LoadPatientHistoryAsync();
        }
        catch (Exception exception)
        {
            ClinicalNoteStatusText.Text = "Falha ao salvar prontuario.";
            MessageBox.Show(
                this,
                exception.Message,
                "Erro ao salvar prontuario",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CopyClinicalNoteButton_OnClick(object sender, RoutedEventArgs e)
    {
        string patientId = PatientIdTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(patientId))
        {
            MessageBox.Show(
                this,
                "Informe o ID do paciente antes de copiar o prontuario.",
                "Prontuario",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        RehabEasyRecord? selectedRecord = MessagesList.SelectedItem as RehabEasyRecord;
        string report = BuildClinicalReportText(patientId, selectedRecord, GetClinicalNoteContent());

        try
        {
            Clipboard.SetText(report);
            ClinicalNoteStatusText.Text = "Prontuario copiado para a area de transferencia.";
            StatusText.Text = ClinicalNoteStatusText.Text;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Erro ao copiar prontuario",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void InsertExamDataButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (MessagesList.SelectedItem is not RehabEasyRecord record)
        {
            MessageBox.Show(
                this,
                "Selecione um registro de exame para inserir os dados no prontuario.",
                "Prontuario",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string examSection = BuildExamDataSection(record);
        string currentContent = GetClinicalNoteContent();

        ClinicalNoteTextBox.Text = string.IsNullOrWhiteSpace(currentContent)
            ? examSection
            : $"{currentContent.TrimEnd()}{Environment.NewLine}{Environment.NewLine}{examSection}";

        ClinicalNoteTextBox.CaretIndex = ClinicalNoteTextBox.Text.Length;
        ClinicalNoteTextBox.Focus();
    }

    private async Task LoadClinicalNoteForCurrentPatientAsync()
    {
        if (_isLoadingClinicalNote || ClinicalNoteTextBox is null)
        {
            return;
        }

        string patientId = PatientIdTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(patientId))
        {
            ClinicalNoteTextBox.Text = ClinicalNotePlaceholder;
            ClinicalNoteStatusText.Text = "Informe o ID do paciente acima para salvar o prontuario.";
            return;
        }

        _isLoadingClinicalNote = true;
        try
        {
            PatientClinicalNote? note = await _clinicalNoteStore.GetClinicalNoteAsync(patientId, CancellationToken.None);
            if (note is null || string.IsNullOrWhiteSpace(note.Content))
            {
                ClinicalNoteTextBox.Text = ClinicalNotePlaceholder;
                ClinicalNoteStatusText.Text = $"Nenhum prontuario salvo para o paciente {patientId}.";
                return;
            }

            ClinicalNoteTextBox.Text = note.Content;
            ClinicalNoteStatusText.Text =
                $"Prontuario carregado para {patientId}. Ultima atualizacao: {note.UpdatedAt.LocalDateTime:g}.";
        }
        finally
        {
            _isLoadingClinicalNote = false;
        }
    }

    private void SyncPatientIdFromRecord(RehabEasyRecord record)
    {
        string? patientExternalId = PatientRecordHelper.TryGetPatientExternalId(record.RawPayloadJson);
        if (string.IsNullOrWhiteSpace(patientExternalId))
        {
            return;
        }

        // Atualiza o contexto clinico do paciente selecionado sem filtrar a lista geral.
        // O filtro por ID so muda quando o usuario edita o campo e sai dele.
        if (!string.Equals(PatientIdTextBox.Text.Trim(), patientExternalId, StringComparison.OrdinalIgnoreCase))
        {
            PatientIdTextBox.Text = patientExternalId;
        }

        _ = LoadClinicalNoteForCurrentPatientAsync();
        _ = LoadPatientHistoryAsync();
    }

    private string GetClinicalNoteContent()
    {
        string content = ClinicalNoteTextBox.Text.Trim();
        return string.Equals(content, ClinicalNotePlaceholder, StringComparison.Ordinal)
            ? string.Empty
            : content;
    }

    private static string BuildExamDataSection(RehabEasyRecord record)
    {
        StringBuilder builder = new();
        builder.AppendLine("--- DADOS DO EXAME ---");
        builder.AppendLine($"Exame: {record.Title}");
        builder.AppendLine($"Origem: {record.Sender}");
        builder.AppendLine($"Recebido em: {record.ReceivedAt.LocalDateTime:g}");

        string? patientName = PatientRecordHelper.TryGetPatientName(record.RawPayloadJson);
        if (!string.IsNullOrWhiteSpace(patientName))
        {
            builder.AppendLine($"Paciente: {patientName}");
        }

        if (!string.IsNullOrWhiteSpace(record.Summary))
        {
            builder.AppendLine($"Resumo: {record.Summary}");
        }

        builder.AppendLine();
        builder.AppendLine(string.IsNullOrWhiteSpace(record.PlainTextContent)
            ? record.RawPayloadJson
            : record.PlainTextContent);

        return builder.ToString().TrimEnd();
    }

    private string BuildClinicalReportText(string patientId, RehabEasyRecord? selectedRecord, string clinicalNote)
    {
        StringBuilder builder = new();
        builder.AppendLine("EVOLUCAO CLINICA - RehabEasy");
        builder.AppendLine($"Paciente ID: {patientId}");
        builder.AppendLine($"Gerado em: {DateTime.Now:g}");
        builder.AppendLine();

        if (selectedRecord is not null)
        {
            builder.AppendLine(BuildExamDataSection(selectedRecord));
            builder.AppendLine();
            builder.AppendLine("--- INDICADORES ---");
            builder.AppendLine(BuildChartSummaryText());
            builder.AppendLine();
        }

        builder.AppendLine("--- PRONTUARIO / EVOLUCAO ---");
        builder.AppendLine(string.IsNullOrWhiteSpace(clinicalNote)
            ? "(Sem texto de prontuario registrado.)"
            : clinicalNote);

        return builder.ToString().TrimEnd();
    }

    private string BuildChartSummaryText()
    {
        StringBuilder builder = new();
        builder.AppendLine($"{SummaryTitleText.Text}: {AverageTimeText.Text} ({PrimaryMetricLabelText.Text})");
        builder.AppendLine($"Registros: {TestsCountText.Text}");
        builder.AppendLine($"Alerta: {RiskText.Text}");
        builder.AppendLine($"Status: {StatusIndicatorText.Text}");

        if (MainChartBars.ItemsSource is IEnumerable<ClinicalBarChartItem> bars)
        {
            foreach (ClinicalBarChartItem bar in bars)
            {
                builder.AppendLine($"{bar.Label}: {bar.ValueLabel}");
            }
        }

        if (!string.IsNullOrWhiteSpace(ChartNotesText.Text))
        {
            builder.AppendLine($"Observacao: {ChartNotesText.Text}");
        }

        return builder.ToString().TrimEnd();
    }

    private string? GetPatientFilterQuery()
    {
        string patientId = PatientIdTextBox.Text.Trim();
        return string.IsNullOrWhiteSpace(patientId) ? null : patientId;
    }

    private async Task LoadPatientHistoryAsync()
    {
        string patientId = PatientIdTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(patientId))
        {
            _currentPatientHistory = null;
            _lastHistoryReport = string.Empty;
            PatientHistorySummaryText.Text = "Informe o ID do paciente para ver o historico.";
            PatientTimelineList.ItemsSource = Array.Empty<PatientTimelineItem>();
            return;
        }

        try
        {
            PatientHistorySnapshot history =
                await _patientHistoryService.GetPatientHistoryAsync(patientId, CancellationToken.None);
            _currentPatientHistory = history;
            _lastHistoryReport = _patientHistoryService.BuildHistoryReport(history);

            Dictionary<string, int> testCounts = history.Tests
                .GroupBy(test => test.TestType)
                .ToDictionary(group => group.Key, group => group.Count());

            string testSummary = testCounts.Count == 0
                ? "0 testes"
                : string.Join(", ", testCounts.Select(pair => $"{pair.Value} {pair.Key}"));

            PatientHistorySummaryText.Text =
                $"Historico: {testSummary} | {history.ClinicalNotes.Count} versoes de prontuario";

            List<PatientTimelineItem> timelineItems = [];
            foreach (PatientTestHistoryEntry test in history.Tests)
            {
                timelineItems.Add(new PatientTimelineItem
                {
                    Category = "Teste",
                    Headline = $"{test.TestType} - {test.Title}",
                    Subline = $"{test.ReceivedAt.LocalDateTime:g} | {test.MetricsSummary}",
                    OccurredAt = test.ReceivedAt,
                    RecordId = test.RecordId
                });
            }

            foreach (PatientClinicalNoteHistoryEntry note in history.ClinicalNotes)
            {
                string preview = note.Content.Length > 90
                    ? note.Content[..90] + "..."
                    : note.Content;

                timelineItems.Add(new PatientTimelineItem
                {
                    Category = "Prontuario",
                    Headline = "Prontuario salvo",
                    Subline = $"{note.SavedAt.LocalDateTime:g} | {preview}",
                    OccurredAt = note.SavedAt
                });
            }

            PatientTimelineList.ItemsSource = timelineItems
                .OrderByDescending(item => item.OccurredAt)
                .ToList();
        }
        catch (Exception exception)
        {
            PatientHistorySummaryText.Text = "Falha ao carregar historico do paciente.";
            PatientTimelineList.ItemsSource = Array.Empty<PatientTimelineItem>();
            StatusText.Text = exception.Message;
        }
    }

    private async void GenerateHistoryReportButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!await TryEnsureHistoryReportAsync())
        {
            return;
        }

        ClinicalNoteStatusText.Text = "Relatorio de historico gerado.";
        StatusText.Text =
            $"Relatorio pronto para o paciente {PatientIdTextBox.Text.Trim()} ({_currentPatientHistory?.Tests.Count ?? 0} testes). Use Copiar historico.";
    }

    private async void CopyHistoryReportButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!await TryEnsureHistoryReportAsync())
        {
            return;
        }

        try
        {
            Clipboard.SetText(_lastHistoryReport);
            ClinicalNoteStatusText.Text = "Historico completo copiado para a area de transferencia.";
            StatusText.Text = ClinicalNoteStatusText.Text;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Erro ao copiar historico",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void PatientTimelineList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PatientTimelineList.SelectedItem is not PatientTimelineItem timelineItem ||
            string.IsNullOrWhiteSpace(timelineItem.RecordId))
        {
            return;
        }

        RehabEasyRecord? record = _currentRecords.FirstOrDefault(item => item.Id == timelineItem.RecordId);
        if (record is not null)
        {
            MessagesList.SelectedItem = record;
        }
    }

    private async Task<bool> TryEnsureHistoryReportAsync()
    {
        string patientId = PatientIdTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(patientId))
        {
            MessageBox.Show(
                this,
                "Informe o ID do paciente para gerar o relatorio de historico.",
                "Historico do paciente",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        await LoadPatientHistoryAsync();

        if (_currentPatientHistory is null ||
            (_currentPatientHistory.Tests.Count == 0 && _currentPatientHistory.ClinicalNotes.Count == 0))
        {
            MessageBox.Show(
                this,
                "Nenhum historico encontrado para este paciente.",
                "Historico do paciente",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        return true;
    }

    private static bool IsAlphanumeric(string value)
    {
        return value.All(char.IsLetterOrDigit);
    }

    private async Task RefreshFromApiAsync()
    {
        if (_payloadImportService is null)
        {
            MessageBox.Show(
                this,
                StatusText.Text,
                "Configuracao da API",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        RefreshButton.IsEnabled = false;
        RefreshButton.Content = "Atualizando...";
        StatusText.Text = "Buscando payloads pendentes na API...";

        try
        {
            // Mostra a lista geral (todos os pacientes) apos a sincronizacao.
            PatientIdTextBox.Text = string.Empty;
            ClinicalNoteTextBox.Text = ClinicalNotePlaceholder;
            ClinicalNoteStatusText.Text = "Informe o ID do paciente para carregar o prontuario.";
            _currentPatientHistory = null;
            _lastHistoryReport = string.Empty;
            PatientHistorySummaryText.Text = "Informe o ID do paciente para ver o historico.";
            PatientTimelineList.ItemsSource = Array.Empty<PatientTimelineItem>();

            IReadOnlyList<ApiPayloadImportResult> results =
                await _payloadImportService.ImportAllPendingPayloadsAsync(CancellationToken.None);

            if (results.Count == 0)
            {
                await LoadLocalRecordsAsync();
                MessagesList.SelectedIndex = -1;
                StatusText.Text =
                    $"Nenhum payload novo pendente. Lista geral: {_currentRecords.Count} registros locais.";
                return;
            }

            List<RehabEasyRecord> importedRecords = [];
            int pdfCount = 0;
            foreach (ApiPayloadImportResult result in results)
            {
                await _recordStore.SaveRecordsAsync(result.Records, CancellationToken.None);
                importedRecords.AddRange(result.Records);
                if (!string.IsNullOrWhiteSpace(result.PdfLocalPath))
                {
                    pdfCount++;
                }
            }

            await LoadLocalRecordsAsync();
            MessagesList.SelectedIndex = -1;
            SubjectText.Text = "Nenhum registro selecionado";
            MetaText.Text = "Selecione um registro na lista para ver o exame, PDF e graficos.";
            BodyText.Text = "Os dados do exame aparecem aqui.";
            UpdateCharts(null);
            await ClearPdfAsync();

            StatusText.Text =
                $"{results.Count} payload(s) importado(s), {importedRecords.Count} registro(s), " +
                $"{pdfCount} PDF(s). Lista geral com {_currentRecords.Count} registros. " +
                "Selecione um exame ou filtre pelo ID do paciente.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Falha ao atualizar pela API.";
            MessageBox.Show(
                this,
                exception.Message,
                "Erro na integracao com a API",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            RefreshButton.Content = "Atualizar";
        }
    }

    private async Task LoadLocalRecordsAsync()
    {
        if (_recordStore is null)
        {
            return;
        }

        IReadOnlyList<RehabEasyRecord> records = await _recordStore.SearchAsync(GetPatientFilterQuery(), CancellationToken.None);
        _currentRecords = records.ToList();
        ApplyRecordSort();

        if (records.Count == 0)
        {
            StatusText.Text = "Nenhum registro local encontrado.";
            UpdateCharts(null);
            await ClearPdfAsync();
        }
    }

    private void ApplyRecordSort()
    {
        if (MessagesList is null || DateSortComboBox is null)
        {
            return;
        }

        RehabEasyRecord? selectedRecord = MessagesList.SelectedItem as RehabEasyRecord;
        bool ascending = DateSortComboBox.SelectedIndex == 1;
        List<RehabEasyRecord> sortedRecords = ascending
            ? _currentRecords.OrderBy(record => record.ReceivedAt).ToList()
            : _currentRecords.OrderByDescending(record => record.ReceivedAt).ToList();

        MessagesList.ItemsSource = sortedRecords;

        if (selectedRecord is not null)
        {
            MessagesList.SelectedItem = sortedRecords.FirstOrDefault(record => record.Id == selectedRecord.Id);
        }
        else if (sortedRecords.Count > 0 && MessagesList.SelectedIndex < 0)
        {
            MessagesList.SelectedIndex = 0;
        }
    }

    private async Task ShowPdfAsync(string? pdfLocalPath)
    {
        if (PdfWebView is null || PdfPlaceholderText is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(pdfLocalPath) || !File.Exists(pdfLocalPath))
        {
            await ClearPdfAsync();
            return;
        }

        try
        {
            await PdfWebView.EnsureCoreWebView2Async();
            string fileUri = new Uri(pdfLocalPath).AbsoluteUri;
            PdfWebView.Source = new Uri(fileUri);
            PdfWebView.Visibility = Visibility.Visible;
            PdfPlaceholderText.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            PdfWebView.Visibility = Visibility.Collapsed;
            PdfPlaceholderText.Visibility = Visibility.Visible;
            PdfPlaceholderText.Text = $"Nao foi possivel abrir o PDF: {exception.Message}";
        }
    }

    private async Task ClearPdfAsync()
    {
        if (PdfWebView is null || PdfPlaceholderText is null)
        {
            return;
        }

        PdfWebView.Visibility = Visibility.Collapsed;
        PdfPlaceholderText.Visibility = Visibility.Visible;
        PdfPlaceholderText.Text = "Selecione um registro com PDF para visualizar.";

        try
        {
            if (PdfWebView.CoreWebView2 is not null)
            {
                PdfWebView.CoreWebView2.Navigate("about:blank");
            }
        }
        catch
        {
            // Ignore cleanup failures for the embedded browser.
        }

        await Task.CompletedTask;
    }

    private void UpdateCharts(RehabEasyRecord? record)
    {
        TestsCountText.Text = (_currentPatientHistory?.Tests.Count ?? _currentRecords.Count).ToString();

        if (record is null)
        {
            DeleteButton.IsEnabled = false;
            ResetChartLabels();
            AverageTimeText.Text = "--";
            RiskText.Text = "--";
            TugProgressBar.Value = 0;
            StatusIndicatorText.Text = "--";
            ChartNotesText.Text = "Aguardando registros importados da API.";
            RenderChartBars([]);
            return;
        }

        if (TryExtractIndexIndexMetrics(record.RawPayloadJson, out IndexIndexMetrics indexIndexMetrics))
        {
            ApplyIndexIndexChartLabels();
            AverageTimeText.Text = indexIndexMetrics.FinalDistanceMm is double distance
                ? $"{distance:0.#} mm"
                : "--";
            TugProgressBar.Maximum = Math.Max(indexIndexMetrics.TouchThresholdMm ?? 15, 15);
            TugProgressBar.Value = Math.Clamp(
                indexIndexMetrics.FinalDistanceMm ?? 0,
                0,
                TugProgressBar.Maximum);
            RiskText.Text = string.IsNullOrWhiteSpace(indexIndexMetrics.AsymmetryStatus)
                ? "--"
                : indexIndexMetrics.AsymmetryStatus;
            StatusIndicatorText.Text = indexIndexMetrics.HasAlert ? "Alerta" : "OK";
            StatusIndicatorText.Foreground = indexIndexMetrics.HasAlert
                ? (Brush)FindResource("MedWarningBrush")
                : (Brush)FindResource("MedPrimaryBrush");
            ChartNotesText.Text = string.IsNullOrWhiteSpace(indexIndexMetrics.InterpretationNote)
                ? "Indicadores extraidos do relatorio Index-Index selecionado."
                : indexIndexMetrics.InterpretationNote;
            ChartLegendText.Text =
                "Barras: oscilacao (DP) por mao e geral. Distancia final vs limiar de toque.";
            RenderChartBars(BuildIndexIndexBars(indexIndexMetrics));
            return;
        }

        if (TryExtractEquilibrioMetrics(record.RawPayloadJson, out EquilibrioMetrics equilibrioMetrics))
        {
            ApplyEquilibrioChartLabels();
            AverageTimeText.Text = equilibrioMetrics.SplMm is double spl ? $"{spl:0.#} mm" : "--";
            TugProgressBar.Maximum = 500;
            TugProgressBar.Value = Math.Clamp(equilibrioMetrics.SplMm ?? 0, 0, 500);
            RiskText.Text = string.IsNullOrWhiteSpace(equilibrioMetrics.VisualDependencyStatus)
                ? "--"
                : equilibrioMetrics.VisualDependencyStatus;
            StatusIndicatorText.Text = equilibrioMetrics.HasAlert ? "Alerta" : "OK";
            StatusIndicatorText.Foreground = equilibrioMetrics.HasAlert
                ? (Brush)FindResource("MedWarningBrush")
                : (Brush)FindResource("MedPrimaryBrush");
            ChartNotesText.Text = string.IsNullOrWhiteSpace(equilibrioMetrics.InterpretationNote)
                ? "Indicadores extraidos do relatorio de equilibrio selecionado."
                : equilibrioMetrics.InterpretationNote;
            ChartLegendText.Text =
                "Barras: SPL, area da elipse, velocidade e Romberg (limite tipico 2.0).";
            RenderChartBars(BuildEquilibrioBars(equilibrioMetrics));
            return;
        }

        ApplyCvTugChartLabels();
        CvTugMetrics metrics = ExtractCvTugMetrics(record.RawPayloadJson);
        TugProgressBar.Maximum = 20;
        if (metrics.NormalTotalSeconds is double normalSeconds)
        {
            AverageTimeText.Text = $"{normalSeconds:0.0}s";
            TugProgressBar.Value = Math.Clamp(normalSeconds, 0, 20);
        }
        else
        {
            AverageTimeText.Text = "--";
            TugProgressBar.Value = 0;
        }

        RiskText.Text = string.IsNullOrWhiteSpace(metrics.DualTaskStatus) ? "--" : metrics.DualTaskStatus;
        StatusIndicatorText.Text = metrics.HasAlert ? "Alerta" : "OK";
        StatusIndicatorText.Foreground = metrics.HasAlert
            ? (Brush)FindResource("MedWarningBrush")
            : (Brush)FindResource("MedPrimaryBrush");
        ChartNotesText.Text = string.IsNullOrWhiteSpace(metrics.WalkSpeedNote)
            ? "Indicadores extraidos do payload selecionado."
            : metrics.WalkSpeedNote;
        ChartLegendText.Text =
            "Barras: tempos TUG (Normal/Motora/Cognitiva) e DTC pior em %.";
        RenderChartBars(BuildCvTugBars(metrics));
    }

    private void ResetChartLabels()
    {
        SummaryTitleText.Text = "Resumo";
        PrimaryMetricLabelText.Text = "Indicador principal";
        PrimaryMetricReferenceText.Text = "Referencia visual conforme o tipo de exame.";
        ChartPanelTitleText.Text = "Graficos do exame";
        ChartLegendText.Text = "Selecione um exame para ver os graficos.";
        TugProgressBar.Maximum = 20;
        StatusIndicatorText.Foreground = (Brush)FindResource("MedTextPrimaryBrush");
    }

    private void ApplyCvTugChartLabels()
    {
        SummaryTitleText.Text = "Resumo TUG";
        PrimaryMetricLabelText.Text = "Tempo normal";
        PrimaryMetricReferenceText.Text = "Referencia visual: 0 a 20 segundos";
        ChartPanelTitleText.Text = "Tempos e custo dual-task";
    }

    private void ApplyEquilibrioChartLabels()
    {
        SummaryTitleText.Text = "Resumo Equilibrio";
        PrimaryMetricLabelText.Text = "SPL (mm)";
        PrimaryMetricReferenceText.Text = "Referencia visual: 0 a 500 mm";
        ChartPanelTitleText.Text = "Indices posturograficos";
    }

    private void ApplyIndexIndexChartLabels()
    {
        SummaryTitleText.Text = "Resumo Index-Index";
        PrimaryMetricLabelText.Text = "Distancia final (mm)";
        PrimaryMetricReferenceText.Text = "Referencia visual: limiar de toque do exame";
        ChartPanelTitleText.Text = "Oscilacao e toque";
    }

    private void RenderChartBars(IReadOnlyList<ClinicalBarChartItem> bars)
    {
        MainChartBars.ItemsSource = bars;
    }

    private IReadOnlyList<ClinicalBarChartItem> BuildCvTugBars(CvTugMetrics metrics)
    {
        const double chartMaxHeight = 110;
        Brush primary = (Brush)FindResource("MedPrimaryBrush");
        Brush accent = (Brush)FindResource("MedAccentBrush");
        Brush warning = (Brush)FindResource("MedWarningBrush");

        List<(string Label, double? Value, string Suffix, Brush Fill, string Tip)> series =
        [
            ("Normal", metrics.NormalTotalSeconds, "s", primary, "Tempo total na condicao Normal"),
            ("Motora", metrics.MotorTotalSeconds, "s", accent, "Tempo total na condicao Motora"),
            ("Cognitiva", metrics.CognitiveTotalSeconds, "s", accent, "Tempo total na condicao Cognitiva"),
            ("DTC", metrics.WorstDualTaskCostPercent, "%", warning, "Pior dual-task cost entre as condicoes")
        ];

        double maxValue = series
            .Select(item => item.Value ?? 0)
            .DefaultIfEmpty(1)
            .Max();
        maxValue = Math.Max(maxValue, 1);

        return series
            .Select(item =>
            {
                double value = item.Value ?? 0;
                return new ClinicalBarChartItem
                {
                    Label = item.Label,
                    ValueLabel = item.Value is double parsed
                        ? $"{parsed:0.#}{item.Suffix}"
                        : "--",
                    BarHeight = Math.Max(6, chartMaxHeight * (value / maxValue)),
                    Fill = item.Fill,
                    Tooltip = item.Tip
                };
            })
            .ToList();
    }

    private IReadOnlyList<ClinicalBarChartItem> BuildEquilibrioBars(EquilibrioMetrics metrics)
    {
        const double chartMaxHeight = 110;
        Brush primary = (Brush)FindResource("MedPrimaryBrush");
        Brush accent = (Brush)FindResource("MedAccentBrush");
        Brush warning = (Brush)FindResource("MedWarningBrush");

        List<(string Label, double? Value, string Format, Brush Fill, string Tip, double ScaleCap)> series =
        [
            ("SPL", metrics.SplMm, "0", primary, "Comprimento de trajetoria (mm)", 500),
            ("Area", metrics.EllipseAreaMm2, "0", accent, "Area da elipse 95% (mm2)", 500),
            ("Vel.", metrics.MeanOscillationVelocityMmS, "0.##", accent, "Velocidade media (mm/s)", 30),
            ("Romberg", metrics.RombergAreaQuotient, "0.##", warning, "Quociente de Romberg area (limite ~2.0)", 4)
        ];

        return series
            .Select(item =>
            {
                double value = item.Value ?? 0;
                double ratio = Math.Clamp(value / Math.Max(item.ScaleCap, 0.01), 0, 1);
                return new ClinicalBarChartItem
                {
                    Label = item.Label,
                    ValueLabel = item.Value is double parsed
                        ? parsed.ToString(item.Format)
                        : "--",
                    BarHeight = Math.Max(6, chartMaxHeight * ratio),
                    Fill = item.Value is double romberg &&
                           string.Equals(item.Label, "Romberg", StringComparison.Ordinal) &&
                           romberg >= 2.0
                        ? warning
                        : item.Fill,
                    Tooltip = item.Tip
                };
            })
            .ToList();
    }

    private IReadOnlyList<ClinicalBarChartItem> BuildIndexIndexBars(IndexIndexMetrics metrics)
    {
        const double chartMaxHeight = 110;
        Brush primary = (Brush)FindResource("MedPrimaryBrush");
        Brush accent = (Brush)FindResource("MedAccentBrush");
        Brush warning = (Brush)FindResource("MedWarningBrush");

        List<(string Label, double? Value, Brush Fill, string Tip)> series =
        [
            ("Esq.", metrics.LeftOscillationMm, primary, "Oscilacao mao esquerda (DP)"),
            ("Dir.", metrics.RightOscillationMm, warning, "Oscilacao mao direita (DP)"),
            ("Geral", metrics.OverallOscillationMm, accent, "Oscilacao geral (DP)"),
            ("Dist.", metrics.FinalDistanceMm, primary, "Distancia final entre pontas (mm)"),
            ("Limiar", metrics.TouchThresholdMm, accent, "Limiar de toque configurado (mm)")
        ];

        double maxValue = series
            .Select(item => item.Value ?? 0)
            .DefaultIfEmpty(1)
            .Max();
        maxValue = Math.Max(maxValue, 1);

        return series
            .Select(item =>
            {
                double value = item.Value ?? 0;
                return new ClinicalBarChartItem
                {
                    Label = item.Label,
                    ValueLabel = item.Value is double parsed ? $"{parsed:0.#}" : "--",
                    BarHeight = Math.Max(6, chartMaxHeight * (value / maxValue)),
                    Fill = item.Fill,
                    Tooltip = item.Tip
                };
            })
            .ToList();
    }

    private static bool TryExtractIndexIndexMetrics(string rawPayloadJson, out IndexIndexMetrics metrics)
    {
        metrics = new IndexIndexMetrics();

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawPayloadJson);
            JsonElement root = document.RootElement;

            if (!TryGetProperty(root, "assessment", out JsonElement assessment))
            {
                return false;
            }

            bool looksLikeIndexIndex =
                (TryGetProperty(assessment, "metrics", out JsonElement metricsNode) &&
                 TryGetProperty(metricsNode, "final_fingertip_distance_mm", out _)) ||
                string.Equals(
                    TryGetString(assessment, "test_type"),
                    "INDEX_INDEX",
                    StringComparison.OrdinalIgnoreCase);

            if (!looksLikeIndexIndex)
            {
                return false;
            }

            if (TryGetProperty(assessment, "derived_metrics", out JsonElement derivedMetrics))
            {
                metrics.FinalDistanceMm = TryGetDouble(derivedMetrics, "final_fingertip_distance_mm");
                metrics.LeftOscillationMm = TryGetDouble(derivedMetrics, "left_hand_oscillation_sd_mm");
                metrics.RightOscillationMm = TryGetDouble(derivedMetrics, "right_hand_oscillation_sd_mm");
                metrics.OverallOscillationMm = TryGetDouble(derivedMetrics, "overall_oscillation_sd_mm");
            }

            if (TryGetProperty(assessment, "metrics", out JsonElement rawMetrics))
            {
                metrics.FinalDistanceMm ??= TryGetDouble(rawMetrics, "final_fingertip_distance_mm");
                metrics.LeftOscillationMm ??= TryGetDouble(rawMetrics, "left_hand_oscillation_sd_mm");
                metrics.RightOscillationMm ??= TryGetDouble(rawMetrics, "right_hand_oscillation_sd_mm");
                metrics.OverallOscillationMm ??= TryGetDouble(rawMetrics, "overall_oscillation_sd_mm");
                metrics.TouchThresholdMm = TryGetDouble(rawMetrics, "touch_threshold_mm");
            }

            if (TryGetProperty(assessment, "protocol", out JsonElement protocol))
            {
                metrics.TouchThresholdMm ??= TryGetDouble(protocol, "touch_threshold_mm");
            }

            if (TryGetProperty(assessment, "automated_flags", out JsonElement automatedFlags))
            {
                if (TryGetProperty(automatedFlags, "hand_asymmetry", out JsonElement asymmetry))
                {
                    metrics.AsymmetryStatus = TryGetString(asymmetry, "status");
                    metrics.HasAlert = metrics.AsymmetryStatus?
                        .Contains("ALERTA", StringComparison.OrdinalIgnoreCase) == true;
                }

                bool? touchOk = TryGetBool(automatedFlags, "touch_within_threshold");
                if (touchOk == false)
                {
                    metrics.HasAlert = true;
                }
            }

            metrics.InterpretationNote = TryGetString(assessment, "interpretation");
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryExtractEquilibrioMetrics(string rawPayloadJson, out EquilibrioMetrics metrics)
    {
        metrics = new EquilibrioMetrics();

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawPayloadJson);
            JsonElement root = document.RootElement;

            if (!TryGetProperty(root, "assessment", out JsonElement assessment) ||
                !TryGetProperty(assessment, "posturographic_indices", out _))
            {
                return false;
            }

            if (TryGetProperty(assessment, "derived_metrics", out JsonElement derivedMetrics))
            {
                metrics.SplMm = TryGetDouble(derivedMetrics, "spl_mm");
                metrics.EllipseAreaMm2 = TryGetDouble(derivedMetrics, "confidence_ellipse_95_area_mm2");
                metrics.MeanOscillationVelocityMmS = TryGetDouble(
                    derivedMetrics,
                    "mean_oscillation_velocity_mm_s");
                metrics.RombergAreaQuotient = TryGetDouble(derivedMetrics, "romberg_area_quotient");
                metrics.ApMlRatio = TryGetDouble(derivedMetrics, "ap_ml_ratio");
            }

            if (TryGetProperty(assessment, "automated_flags", out JsonElement automatedFlags))
            {
                if (TryGetProperty(automatedFlags, "visual_dependency", out JsonElement visualDependency))
                {
                    metrics.VisualDependencyStatus = TryGetString(visualDependency, "status");
                    metrics.RombergAreaQuotient ??= TryGetDouble(visualDependency, "romberg_area_quotient");
                    metrics.HasAlert = metrics.VisualDependencyStatus?
                        .Contains("ALERTA", StringComparison.OrdinalIgnoreCase) == true;
                }

                metrics.HasAlert = metrics.HasAlert ||
                    TryGetBool(automatedFlags, "increased_postural_sway") == true;
            }

            metrics.InterpretationNote = TryGetString(assessment, "interpretation");
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool? TryGetBool(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static CvTugMetrics ExtractCvTugMetrics(string rawPayloadJson)
    {
        CvTugMetrics metrics = new();

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawPayloadJson);
            JsonElement root = document.RootElement;

            if (TryGetProperty(root, "assessment", out JsonElement assessment))
            {
                if (TryGetProperty(assessment, "conditions", out JsonElement conditions) &&
                    conditions.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement condition in conditions.EnumerateArray())
                    {
                        string? code = TryGetString(condition, "code");
                        double? totalSeconds = TryGetDouble(condition, "total_seconds");
                        double? dtc = TryGetDouble(condition, "dual_task_cost_percent");

                        if (string.Equals(code, "normal", StringComparison.OrdinalIgnoreCase))
                        {
                            metrics.NormalTotalSeconds = totalSeconds;
                        }
                        else if (string.Equals(code, "motor", StringComparison.OrdinalIgnoreCase))
                        {
                            metrics.MotorTotalSeconds = totalSeconds;
                            metrics.MotorDualTaskCostPercent = dtc;
                        }
                        else if (string.Equals(code, "cognitive", StringComparison.OrdinalIgnoreCase))
                        {
                            metrics.CognitiveTotalSeconds = totalSeconds;
                            metrics.CognitiveDualTaskCostPercent = dtc;
                        }
                    }
                }

                if (TryGetProperty(assessment, "derived_metrics", out JsonElement derivedMetrics))
                {
                    metrics.WorstDualTaskCostPercent = TryGetDouble(derivedMetrics, "worst_dual_task_cost_percent");
                    metrics.NormalWalkSpeedMps = TryGetDouble(derivedMetrics, "normal_walk_speed_mps");
                }

                if (TryGetProperty(assessment, "automated_flags", out JsonElement automatedFlags))
                {
                    if (TryGetProperty(automatedFlags, "dual_task_cost", out JsonElement dualTaskCost))
                    {
                        metrics.WorstDualTaskCostPercent ??= TryGetDouble(dualTaskCost, "worst_percent");
                        metrics.DualTaskStatus = TryGetString(dualTaskCost, "status");
                    }

                    if (TryGetProperty(automatedFlags, "gait_speed", out JsonElement gaitSpeed))
                    {
                        metrics.NormalWalkSpeedMps ??= TryGetDouble(gaitSpeed, "normal_condition_mps");
                        metrics.WalkSpeedNote = TryGetString(gaitSpeed, "note");
                    }

                    metrics.HasAlert = metrics.DualTaskStatus?.Contains("ALERTA", StringComparison.OrdinalIgnoreCase) == true;
                }

                if (TryGetProperty(assessment, "flags", out JsonElement flags))
                {
                    metrics.WorstDualTaskCostPercent ??= TryGetDouble(flags, "worst_dual_task_cost_percent");
                    metrics.DualTaskStatus ??= TryGetString(flags, "dual_task_cost_status");
                    metrics.NormalWalkSpeedMps ??= TryGetDouble(flags, "normal_walk_speed_mps");
                    metrics.WalkSpeedNote ??= TryGetString(flags, "walk_speed_note");
                    metrics.HasAlert = metrics.HasAlert ||
                        metrics.DualTaskStatus?.Contains("ALERTA", StringComparison.OrdinalIgnoreCase) == true;
                }
            }
        }
        catch (JsonException)
        {
            return metrics;
        }

        return metrics;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out JsonElement value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double parsed)
            ? parsed
            : null;
    }

    private void ApplyMissingConfigurationState(string configurationMessage)
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = configurationMessage;
        BodyText.Text = $"Configure {SystemBApiKeyEnv} e reinicie o aplicativo para habilitar a importacao.";
    }

    private static bool TryCreateApiPayloadImportService(
        out IApiPayloadImportService? importService,
        out string configurationMessage)
    {
        importService = null;
        string apiKey = Environment.GetEnvironmentVariable(SystemBApiKeyEnv) ?? DefaultSystemBApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            configurationMessage = $"API key ausente. Defina {SystemBApiKeyEnv} com a chave do Sistema B.";
            return false;
        }

        string baseUrl = Environment.GetEnvironmentVariable(ApiBaseUrlEnv) ?? DefaultApiBaseUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? apiUri))
        {
            configurationMessage = $"URL da API invalida em {ApiBaseUrlEnv}: {baseUrl}";
            return false;
        }

        HttpClient httpClient = new()
        {
            BaseAddress = apiUri
        };

        importService = new ApiPayloadImportService(httpClient, apiKey);
        configurationMessage = string.Empty;
        return true;
    }

    private static string GetDatabasePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RehabEasy",
            "rehabeasy.db");
    }

    private sealed class CvTugMetrics
    {
        public double? NormalTotalSeconds { get; set; }
        public double? MotorTotalSeconds { get; set; }
        public double? CognitiveTotalSeconds { get; set; }
        public double? MotorDualTaskCostPercent { get; set; }
        public double? CognitiveDualTaskCostPercent { get; set; }
        public double? WorstDualTaskCostPercent { get; set; }
        public string? DualTaskStatus { get; set; }
        public double? NormalWalkSpeedMps { get; set; }
        public string? WalkSpeedNote { get; set; }
        public bool HasAlert { get; set; }
    }

    private sealed class EquilibrioMetrics
    {
        public double? SplMm { get; set; }
        public double? EllipseAreaMm2 { get; set; }
        public double? MeanOscillationVelocityMmS { get; set; }
        public double? RombergAreaQuotient { get; set; }
        public double? ApMlRatio { get; set; }
        public string? VisualDependencyStatus { get; set; }
        public string? InterpretationNote { get; set; }
        public bool HasAlert { get; set; }
    }

    private sealed class IndexIndexMetrics
    {
        public double? FinalDistanceMm { get; set; }
        public double? TouchThresholdMm { get; set; }
        public double? LeftOscillationMm { get; set; }
        public double? RightOscillationMm { get; set; }
        public double? OverallOscillationMm { get; set; }
        public string? AsymmetryStatus { get; set; }
        public string? InterpretationNote { get; set; }
        public bool HasAlert { get; set; }
    }
}
