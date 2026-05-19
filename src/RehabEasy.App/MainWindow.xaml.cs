using System.Net.Http;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
    private const string SearchPlaceholder = "Buscar por titulo, origem, destino ou conteudo";

    private readonly IApiPayloadImportService? _payloadImportService;
    private readonly IRecordStore _recordStore;

    public MainWindow()
    {
        InitializeComponent();

        SqliteRecordStore sqliteRecordStore = new(GetDatabasePath());
        sqliteRecordStore.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        _recordStore = sqliteRecordStore;

        SearchTextBox.Text = SearchPlaceholder;
        LoadLocalRecordsAsync().GetAwaiter().GetResult();

        if (!TryCreateApiPayloadImportService(out IApiPayloadImportService? importService, out string configurationMessage))
        {
            ApplyMissingConfigurationState(configurationMessage);
            return;
        }

        _payloadImportService = importService;
    }

    private async void ConnectButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ImportPayloadAsync();
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        await LoadLocalRecordsAsync();
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
    }

    private async void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        await LoadLocalRecordsAsync();
    }

    private async Task ImportPayloadAsync()
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

        string payloadId = PayloadIdTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(payloadId))
        {
            MessageBox.Show(
                this,
                "Informe o ID do payload criado pela API.",
                "Payload obrigatorio",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ConnectButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        ConnectButton.Content = "Importando...";
        StatusText.Text = "Consumindo payload na API e gravando no SQLite local...";

        try
        {
            ApiPayloadImportResult result = await _payloadImportService.ImportPayloadAsync(payloadId, CancellationToken.None);
            await _recordStore.SaveRecordsAsync(result.Records, CancellationToken.None);

            PayloadIdTextBox.Clear();
            await LoadLocalRecordsAsync();
            MessagesList.SelectedIndex = result.Records.Count > 0 ? 0 : -1;

            StatusText.Text = $"Payload {result.PayloadId} importado de {result.SourceName}: {result.Records.Count} registros gravados.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Falha ao importar payload pela API.";
            MessageBox.Show(
                this,
                exception.Message,
                "Erro na integracao com a API",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            ConnectButton.IsEnabled = true;
            RefreshButton.IsEnabled = true;
            ConnectButton.Content = "Importar payload";
        }
    }

    private async Task LoadLocalRecordsAsync()
    {
        if (_recordStore is null)
        {
            return;
        }

        string query = SearchTextBox.Text;
        if (string.Equals(query, SearchPlaceholder, StringComparison.Ordinal))
        {
            query = string.Empty;
        }

        IReadOnlyList<RehabEasyRecord> records = await _recordStore.SearchAsync(query, CancellationToken.None);
        MessagesList.ItemsSource = records;

        if (records.Count == 0)
        {
            StatusText.Text = "Nenhum registro local encontrado.";
        }
    }

    private void ApplyMissingConfigurationState(string configurationMessage)
    {
        ConnectButton.IsEnabled = false;
        PayloadIdTextBox.IsEnabled = false;
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
}
