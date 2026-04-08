using System.Globalization;
using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using RebEasy.Domain.Contracts;
using RebEasy.Domain.Models;

namespace RebEasy.Infrastructure.Services;

public sealed class GmailSyncService : IGmailSyncService
{
    private static readonly string[] Scopes = [GmailService.Scope.GmailReadonly];
    private const string RequiredSubject = "Relatorio TUG";
    private readonly ClientSecrets _clientSecrets;
    private readonly string _tokenDirectory;

    public GmailSyncService(string clientId, string clientSecret, string tokenDirectory)
    {
        _clientSecrets = new ClientSecrets
        {
            ClientId = clientId,
            ClientSecret = clientSecret
        };
        _tokenDirectory = tokenDirectory;
    }

    public async Task<GmailSyncResult> RunInitialSyncAsync(string? accountEmail, CancellationToken cancellationToken)
    {
        GmailService service = await CreateServiceAsync(accountEmail, cancellationToken);
        Profile profile = await service.Users.GetProfile("me").ExecuteAsync(cancellationToken);

        List<Message> messageRefs = [];
        List<Message> fullMessages = [];

        string? nextPageToken = null;

        do
        {
            UsersResource.MessagesResource.ListRequest request = service.Users.Messages.List("me");
            request.MaxResults = 500;
            request.IncludeSpamTrash = false;
            request.PageToken = nextPageToken;

            ListMessagesResponse listResponse = await request.ExecuteAsync(cancellationToken);

            if (listResponse.Messages is not null)
            {
                messageRefs.AddRange(listResponse.Messages);
            }

            nextPageToken = listResponse.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(nextPageToken));

        foreach (Message item in messageRefs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            UsersResource.MessagesResource.GetRequest messageRequest = service.Users.Messages.Get("me", item.Id);
            messageRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
            Message? fullMessage = await messageRequest.ExecuteAsync(cancellationToken);

            if (fullMessage is not null)
            {
                fullMessages.Add(fullMessage);
            }
        }

        IReadOnlyList<EmailMessage> normalizedMessages = fullMessages
            .Select(MapMessage)
            .Where(message => string.Equals(message.Subject, RequiredSubject, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(message => message.ReceivedAt)
            .ToList();

        ulong? lastHistoryIdValue = fullMessages
            .Select(message => message.HistoryId)
            .Where(historyId => historyId.HasValue)
            .Select(historyId => Convert.ToUInt64(historyId!.Value, CultureInfo.InvariantCulture))
            .OrderByDescending(historyId => historyId)
            .FirstOrDefault();

        return new GmailSyncResult
        {
            AccountEmail = profile.EmailAddress ?? string.Empty,
            LastHistoryId = lastHistoryIdValue?.ToString(CultureInfo.InvariantCulture),
            SyncedAt = DateTimeOffset.UtcNow,
            Messages = normalizedMessages
        };
    }

    public Task<GmailSyncResult> RunIncrementalSyncAsync(SyncState state, CancellationToken cancellationToken)
    {
        return RunInitialSyncAsync(state.AccountEmail, cancellationToken);
    }

    private async Task<GmailService> CreateServiceAsync(string? accountEmail, CancellationToken cancellationToken)
    {
        string userKey = string.IsNullOrWhiteSpace(accountEmail) ? "gmail-default" : SanitizeForPath(accountEmail);
        UserCredential credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            _clientSecrets,
            Scopes,
            userKey,
            cancellationToken,
            new FileDataStore(_tokenDirectory, true));

        return new GmailService(
            new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "RebEasy"
            });
    }

    private static string SanitizeForPath(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        StringBuilder builder = new(value.Length);

        foreach (char character in value.Trim().ToLowerInvariant())
        {
            builder.Append(invalidChars.Contains(character) ? '_' : character);
        }

        return builder.ToString();
    }

    private static EmailMessage MapMessage(Message message)
    {
        MessagePart? payload = message.Payload;
        IReadOnlyDictionary<string, string> headers = (payload?.Headers ?? [])
            .Where(header => !string.IsNullOrWhiteSpace(header.Name))
            .GroupBy(header => header.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        string plainTextBody = FindBody(payload, "text/plain");
        string htmlBody = FindBody(payload, "text/html");

        return new EmailMessage
        {
            Id = message.Id ?? string.Empty,
            ThreadId = message.ThreadId ?? string.Empty,
            Subject = headers.GetValueOrDefault("Subject") ?? "(sem assunto)",
            From = headers.GetValueOrDefault("From") ?? string.Empty,
            To = headers.GetValueOrDefault("To") ?? string.Empty,
            ReceivedAt = ParseInternalDate(message.InternalDate),
            Snippet = message.Snippet ?? string.Empty,
            PlainTextBody = plainTextBody,
            HtmlBody = htmlBody,
            Labels = (message.LabelIds ?? []).ToList()
        };
    }

    private static DateTimeOffset ParseInternalDate(long? internalDate)
    {
        if (!internalDate.HasValue)
        {
            return DateTimeOffset.UtcNow;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(internalDate.Value);
    }

    private static string FindBody(MessagePart? part, string mimeType)
    {
        if (part is null)
        {
            return string.Empty;
        }

        if (string.Equals(part.MimeType, mimeType, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(part.Body?.Data))
        {
            return DecodeBase64Url(part.Body.Data);
        }

        if (part.Parts is null)
        {
            return string.Empty;
        }

        foreach (MessagePart child in part.Parts)
        {
            string body = FindBody(child, mimeType);
            if (!string.IsNullOrWhiteSpace(body))
            {
                return body;
            }
        }

        return string.Empty;
    }

    private static string DecodeBase64Url(string input)
    {
        string normalized = input.Replace('-', '+').Replace('_', '/');
        int padding = 4 - normalized.Length % 4;

        if (padding is > 0 and < 4)
        {
            normalized = normalized.PadRight(normalized.Length + padding, '=');
        }

        byte[] bytes = Convert.FromBase64String(normalized);
        return Encoding.UTF8.GetString(bytes);
    }
}
