using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.IO;
using RebEasy.Domain.Contracts;
using RebEasy.Domain.Models;
using RebEasy.Infrastructure.Services;

namespace RebEasy.App;

public partial class MainWindow : Window
{
    private const string GoogleClientIdEnv = "REBEASY_GOOGLE_CLIENT_ID";
    private const string GoogleClientSecretEnv = "REBEASY_GOOGLE_CLIENT_SECRET";
    private readonly IGmailSyncService? _gmailSyncService;
    private readonly IMessageCache _messageCache;
    private List<EmailMessage> _currentMessages = [];

    public MainWindow()
    {
        InitializeComponent();

        PatientIdTextBox.Text = "PAC2026001";
        DataObject.AddPastingHandler(PatientIdTextBox, PatientIdTextBox_OnPaste);

        string? googleClientId = Environment.GetEnvironmentVariable(GoogleClientIdEnv);
        string? googleClientSecret = Environment.GetEnvironmentVariable(GoogleClientSecretEnv);
        _messageCache = new InMemoryMessageCache();
        LoadDemoMessages();

        if (string.IsNullOrWhiteSpace(googleClientId) || string.IsNullOrWhiteSpace(googleClientSecret))
        {
            ConnectButton.IsEnabled = false;
            StatusText.Text =
                $"Modo demonstracao. Para conectar o Gmail, defina {GoogleClientIdEnv} e {GoogleClientSecretEnv}.";
            return;
        }

        string tokenDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RebEasy",
            "tokens");

        _gmailSyncService = new GmailSyncService(googleClientId, googleClientSecret, tokenDirectory);
        StatusText.Text = "Dados demonstrativos carregados. Conecte o Gmail para sincronizar mensagens reais.";
    }

    private async void ConnectButton_OnClick(object sender, RoutedEventArgs e)
    {
        await SyncMailboxAsync(isRefresh: false);
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        await SyncMailboxAsync(isRefresh: true);
    }

    private void MessagesList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MessagesList.SelectedItem is not EmailMessage message)
        {
            return;
        }

        SubjectText.Text = message.Subject;
        MetaText.Text = $"{message.From} -> {message.To} | {message.ReceivedAt.LocalDateTime:g}";
        BodyText.Text = string.IsNullOrWhiteSpace(message.PlainTextBody)
            ? message.Snippet
            : message.PlainTextBody;
    }

    private void DateSortComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        ApplyMessageSort();
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

    private async Task SyncMailboxAsync(bool isRefresh)
    {
        if (_gmailSyncService is null)
        {
            StatusText.Text = $"Credenciais Google nao configuradas. Defina {GoogleClientIdEnv} e {GoogleClientSecretEnv}.";
            return;
        }

        ConnectButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        ConnectButton.Content = isRefresh ? "Conectado" : "Sincronizando...";
        RefreshButton.Content = "Atualizando...";
        StatusText.Text = isRefresh ? "Atualizando e-mails..." : "Abrindo login do Google...";

        try
        {
            SyncState? existingState = await _messageCache.GetSyncStateAsync(CancellationToken.None);
            GmailSyncResult result = isRefresh && existingState is not null
                ? await _gmailSyncService.RunIncrementalSyncAsync(existingState, CancellationToken.None)
                : await _gmailSyncService.RunInitialSyncAsync(null, CancellationToken.None);

            await _messageCache.SaveMessagesAsync(result.Messages, CancellationToken.None);
            await _messageCache.SaveSyncStateAsync(
                new SyncState
                {
                    AccountEmail = result.AccountEmail,
                    LastHistoryId = result.LastHistoryId,
                    LastSyncedAt = result.SyncedAt
                },
                CancellationToken.None);

            _currentMessages = result.Messages.ToList();
            ApplyMessageSort();
            RefreshButton.IsEnabled = true;
            StatusText.Text = isRefresh
                ? $"Atualizado: {result.AccountEmail} | {result.Messages.Count} emails carregados"
                : $"Conta conectada: {result.AccountEmail} | {result.Messages.Count} emails carregados";
            ConnectButton.Content = "Conectado";
        }
        catch (Exception exception)
        {
            StatusText.Text = isRefresh
                ? "Falha ao atualizar os e-mails."
                : "Falha ao conectar com o Gmail.";
            ConnectButton.Content = "Conectar Gmail";
            MessageBox.Show(
                this,
                exception.Message,
                isRefresh ? "Erro ao atualizar Gmail" : "Erro ao conectar Gmail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            ConnectButton.IsEnabled = true;
            RefreshButton.Content = "Atualizar";

            if (_currentMessages.Count > 0 && _gmailSyncService is not null)
            {
                RefreshButton.IsEnabled = true;
            }
        }
    }

    private void LoadDemoMessages()
    {
        _currentMessages =
        [
            new EmailMessage
            {
                Id = "demo-001",
                ThreadId = "demo-thread-001",
                Subject = "Relatorio TUG - avaliacao inicial",
                From = "clinica.demo@rebeasy.local",
                To = "avaliacao@rebeasy.local",
                ReceivedAt = DateTimeOffset.Now.AddDays(-1).AddHours(-2),
                Snippet = "Paciente realizou cinco execucoes do teste TUG.",
                PlainTextBody =
                    "Teste TUG - avaliacao inicial\n\n" +
                    "Execucoes realizadas: 5\n" +
                    "Tempo medio: 11,8 segundos\n" +
                    "Melhor tempo: 10,9 segundos\n" +
                    "Observacoes: paciente levantou sem apoio em quatro execucoes e apresentou pequena oscilacao na virada.\n" +
                    "Classificacao demonstrativa: risco moderado."
            },
            new EmailMessage
            {
                Id = "demo-002",
                ThreadId = "demo-thread-002",
                Subject = "Relatorio TUG - retorno semanal",
                From = "fisioterapia.demo@rebeasy.local",
                To = "avaliacao@rebeasy.local",
                ReceivedAt = DateTimeOffset.Now.AddDays(-4).AddHours(-1),
                Snippet = "Retorno com reducao leve no tempo medio.",
                PlainTextBody =
                    "Teste TUG - retorno semanal\n\n" +
                    "Execucoes realizadas: 4\n" +
                    "Tempo medio: 12,4 segundos\n" +
                    "Melhor tempo: 11,7 segundos\n" +
                    "Observacoes: marcha estavel, porem com hesitacao no inicio do movimento.\n" +
                    "Classificacao demonstrativa: acompanhamento recomendado."
            },
            new EmailMessage
            {
                Id = "demo-003",
                ThreadId = "demo-thread-003",
                Subject = "Relatorio TUG - pre-alta",
                From = "laboratorio.demo@rebeasy.local",
                To = "avaliacao@rebeasy.local",
                ReceivedAt = DateTimeOffset.Now.AddDays(-9).AddHours(-3),
                Snippet = "Avaliacao de pre-alta com melhora de regularidade.",
                PlainTextBody =
                    "Teste TUG - pre-alta\n\n" +
                    "Execucoes realizadas: 3\n" +
                    "Tempo medio: 10,6 segundos\n" +
                    "Melhor tempo: 10,1 segundos\n" +
                    "Observacoes: execucoes consistentes, sem perda de equilibrio observada.\n" +
                    "Classificacao demonstrativa: boa evolucao funcional."
            }
        ];

        ApplyMessageSort();
        MessagesList.SelectedIndex = 0;
    }

    private void ApplyMessageSort()
    {
        if (MessagesList is null || DateSortComboBox is null)
        {
            return;
        }

        EmailMessage? selectedMessage = MessagesList.SelectedItem as EmailMessage;
        bool ascending = DateSortComboBox.SelectedIndex == 1;
        List<EmailMessage> sortedMessages = ascending
            ? _currentMessages.OrderBy(message => message.ReceivedAt).ToList()
            : _currentMessages.OrderByDescending(message => message.ReceivedAt).ToList();

        MessagesList.ItemsSource = sortedMessages;

        if (selectedMessage is not null)
        {
            MessagesList.SelectedItem = sortedMessages.FirstOrDefault(message => message.Id == selectedMessage.Id);
        }
    }
}
