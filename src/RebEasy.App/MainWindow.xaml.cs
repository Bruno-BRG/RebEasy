using System.Windows.Controls;
using System.Windows;
using System.IO;
using RebEasy.Domain.Contracts;
using RebEasy.Domain.Models;
using RebEasy.Infrastructure.Services;

namespace RebEasy.App;

public partial class MainWindow : Window
{
    private const string GoogleClientIdEnv = "REBEASY_GOOGLE_CLIENT_ID";
    private const string GoogleClientSecretEnv = "REBEASY_GOOGLE_CLIENT_SECRET";
    private readonly IGmailSyncService _gmailSyncService;
    private readonly IMessageCache _messageCache;
    private const string SearchPlaceholder = "Buscar por assunto, remetente ou trecho";

    public MainWindow()
    {
        InitializeComponent();

        string? googleClientId = Environment.GetEnvironmentVariable(GoogleClientIdEnv);
        string? googleClientSecret = Environment.GetEnvironmentVariable(GoogleClientSecretEnv);

        if (string.IsNullOrWhiteSpace(googleClientId) || string.IsNullOrWhiteSpace(googleClientSecret))
        {
            throw new InvalidOperationException(
                $"Google OAuth credentials not configured. Set {GoogleClientIdEnv} and {GoogleClientSecretEnv} environment variables.");
        }

        string tokenDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RebEasy",
            "tokens");

        _gmailSyncService = new GmailSyncService(googleClientId, googleClientSecret, tokenDirectory);
        _messageCache = new InMemoryMessageCache();
        SearchTextBox.Text = SearchPlaceholder;
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

    private async void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_messageCache is null)
        {
            return;
        }

        string query = SearchTextBox.Text;
        if (string.Equals(query, SearchPlaceholder, StringComparison.Ordinal))
        {
            query = string.Empty;
        }

        MessagesList.ItemsSource = await _messageCache.SearchAsync(query, CancellationToken.None);
    }

    private async Task SyncMailboxAsync(bool isRefresh)
    {
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

            MessagesList.ItemsSource = result.Messages;
            MessagesList.SelectedIndex = result.Messages.Count > 0 ? 0 : -1;
            SearchTextBox.IsEnabled = true;
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

            if (SearchTextBox.IsEnabled)
            {
                RefreshButton.IsEnabled = true;
            }
        }
    }
}
