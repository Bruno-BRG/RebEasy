using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private List<RehabEasyRecord> _currentRecords = [];

    public MainWindow()
    {
        InitializeComponent();

        SqliteRecordStore sqliteRecordStore = new(GetDatabasePath());
        sqliteRecordStore.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        _recordStore = sqliteRecordStore;

        PatientIdTextBox.Text = "PAC2026001";
        DataObject.AddPastingHandler(PatientIdTextBox, PatientIdTextBox_OnPaste);
        LoadLocalRecordsAsync().GetAwaiter().GetResult();

        if (!TryCreateApiPayloadImportService(out IApiPayloadImportService? importService, out string configurationMessage))
        {
            ApplyMissingConfigurationState(configurationMessage);
            return;
        }

        _payloadImportService = importService;
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
        UpdateCharts(record);
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
        StatusText.Text = "Buscando proximo payload pendente na API...";

        try
        {
            ApiPayloadImportResult? result = await _payloadImportService.ImportNextPayloadAsync(CancellationToken.None);
            if (result is null)
            {
                await LoadLocalRecordsAsync();
                StatusText.Text = "Nenhum payload novo pendente na API.";
                return;
            }

            await _recordStore.SaveRecordsAsync(result.Records, CancellationToken.None);

            await LoadLocalRecordsAsync();
            MessagesList.SelectedIndex = result.Records.Count > 0 ? 0 : -1;

            StatusText.Text = $"Payload {result.PayloadId} importado de {result.SourceName}: {result.Records.Count} registros gravados.";
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

        IReadOnlyList<RehabEasyRecord> records = await _recordStore.SearchAsync(null, CancellationToken.None);
        _currentRecords = records.ToList();
        ApplyRecordSort();

        if (records.Count == 0)
        {
            StatusText.Text = "Nenhum registro local encontrado.";
            UpdateCharts(null);
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

    private void UpdateCharts(RehabEasyRecord? record)
    {
        TestsCountText.Text = _currentRecords.Count.ToString();

        if (record is null)
        {
            AverageTimeText.Text = "--";
            RiskText.Text = "--";
            TugProgressBar.Value = 0;
            DtcProgressBar.Value = 0;
            DtcText.Text = "--";
            SpeedProgressBar.Value = 0;
            SpeedText.Text = "--";
            StatusProgressBar.Value = 0;
            StatusIndicatorText.Text = "--";
            ChartNotesText.Text = "Aguardando registros importados da API.";
            return;
        }

        CvTugMetrics metrics = ExtractCvTugMetrics(record.RawPayloadJson);
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
        DtcProgressBar.Value = Math.Clamp(metrics.WorstDualTaskCostPercent ?? 0, 0, 100);
        DtcText.Text = metrics.WorstDualTaskCostPercent is double dtc ? $"{dtc:0}%" : "--";
        SpeedProgressBar.Value = Math.Clamp(metrics.NormalWalkSpeedMps ?? 0, 0, 2);
        SpeedText.Text = metrics.NormalWalkSpeedMps is double speed ? $"{speed:0.00}" : "--";
        StatusProgressBar.Value = metrics.HasAlert ? 100 : 35;
        StatusIndicatorText.Text = metrics.HasAlert ? "Alerta" : "OK";
        ChartNotesText.Text = string.IsNullOrWhiteSpace(metrics.WalkSpeedNote)
            ? "Indicadores extraidos do payload selecionado."
            : metrics.WalkSpeedNote;
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
                        if (string.Equals(code, "normal", StringComparison.OrdinalIgnoreCase) &&
                            TryGetDouble(condition, "total_seconds") is double totalSeconds)
                        {
                            metrics.NormalTotalSeconds = totalSeconds;
                        }
                    }
                }

                if (TryGetProperty(assessment, "flags", out JsonElement flags))
                {
                    metrics.WorstDualTaskCostPercent = TryGetDouble(flags, "worst_dual_task_cost_percent");
                    metrics.DualTaskStatus = TryGetString(flags, "dual_task_cost_status");
                    metrics.NormalWalkSpeedMps = TryGetDouble(flags, "normal_walk_speed_mps");
                    metrics.WalkSpeedNote = TryGetString(flags, "walk_speed_note");
                    metrics.HasAlert = metrics.DualTaskStatus?.Contains("ALERTA", StringComparison.OrdinalIgnoreCase) == true;
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
        public double? WorstDualTaskCostPercent { get; set; }
        public string? DualTaskStatus { get; set; }
        public double? NormalWalkSpeedMps { get; set; }
        public string? WalkSpeedNote { get; set; }
        public bool HasAlert { get; set; }
    }
}
