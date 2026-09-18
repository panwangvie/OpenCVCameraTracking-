using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace OpenCVCameraTracking.Core.Notifications;

public sealed class NotificationService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public async Task<NotificationSendResult> SendTestAsync(
        NotificationChannelSettings channel,
        NotificationMessage message,
        CancellationToken cancellationToken = default)
        => await SendAsync(channel, message, cancellationToken);

    public async Task<IReadOnlyList<NotificationDeliveryResult>> SendAlertAsync(
        IEnumerable<NotificationChannelSettings> channels,
        NotificationMessage message,
        string? eventKey = null,
        CancellationToken cancellationToken = default)
    {
        var enabledChannels = channels
            .Where(channel => channel.Enabled &&
                              (eventKey is null || channel.EventKeys.Any(key =>
                                  string.Equals(key, eventKey, StringComparison.OrdinalIgnoreCase))))
            .Select(channel => channel.DeepClone())
            .ToArray();
        if (enabledChannels.Length == 0)
        {
            return [];
        }

        var results = await Task.WhenAll(enabledChannels.Select(async channel =>
            new NotificationDeliveryResult(channel, await SendAsync(channel, message, cancellationToken))));
        return results;
    }

    public async Task<NotificationSendResult> SendAsync(
        NotificationChannelSettings channel,
        NotificationMessage message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return channel.Kind switch
            {
                NotificationChannelKind.ServerChan => await SendServerChanAsync(channel, message, cancellationToken),
                NotificationChannelKind.Feishu => await SendFeishuAsync(channel, message, cancellationToken),
                NotificationChannelKind.Email => await SendEmailAsync(channel, message, cancellationToken),
                NotificationChannelKind.Telegram => await SendTelegramAsync(channel, message, cancellationToken),
                _ => NotificationSendResult.Failed(NotificationResultCode.UnsupportedType)
            };
        }
        catch (OperationCanceledException)
        {
            return NotificationSendResult.Failed(NotificationResultCode.SendCanceled);
        }
        catch (NotificationException exception)
        {
            return NotificationSendResult.Failed(exception.Code, exception.Arguments);
        }
        catch (Exception exception)
        {
            return GetSafeErrorResult(exception);
        }
    }

    private static async Task<NotificationSendResult> SendServerChanAsync(
        NotificationChannelSettings channel,
        NotificationMessage message,
        CancellationToken cancellationToken)
    {
        var sendKey = Require(channel.ServerChanSendKey, NotificationResultCode.ServerChanKeyRequired);
        string endpoint;
        if (sendKey.StartsWith("sctp", StringComparison.OrdinalIgnoreCase))
        {
            var separatorIndex = sendKey.IndexOf('t', 4);
            if (separatorIndex <= 4)
            {
                throw new NotificationException(NotificationResultCode.ServerChanSc3Invalid);
            }

            var uid = sendKey[4..separatorIndex];
            endpoint = $"https://{uid}.push.ft07.com/send/{Uri.EscapeDataString(sendKey)}.send";
        }
        else
        {
            endpoint = $"https://sctapi.ftqq.com/{Uri.EscapeDataString(sendKey)}.send";
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["title"] = message.Title,
            ["desp"] = message.Body
        });
        using var response = await HttpClient.PostAsync(endpoint, content, cancellationToken);
        var error = await GetApiErrorAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return error is null
                ? NotificationSendResult.Failed(NotificationResultCode.HttpError, (int)response.StatusCode)
                : NotificationSendResult.Failed(NotificationResultCode.RemoteError, error);
        }

        return error is null
            ? NotificationSendResult.Succeeded(NotificationResultCode.ServerChanSent)
            : NotificationSendResult.Failed(NotificationResultCode.RemoteError, error);
    }

    private static async Task<NotificationSendResult> SendFeishuAsync(
        NotificationChannelSettings channel,
        NotificationMessage message,
        CancellationToken cancellationToken)
    {
        var webhookUrl = Require(channel.FeishuWebhookUrl, NotificationResultCode.FeishuWebhookRequired);
        var payload = new Dictionary<string, object?>
        {
            ["msg_type"] = "text",
            ["content"] = new Dictionary<string, string>
            {
                ["text"] = $"{message.Title}\n\n{message.Body}"
            }
        };
        if (!string.IsNullOrWhiteSpace(channel.FeishuSecret))
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var signContent = $"{timestamp}\n{channel.FeishuSecret}";
            // Feishu signs an empty message with timestamp + "\\n" + secret as the HMAC key.
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signContent));
            payload["timestamp"] = timestamp;
            payload["sign"] = Convert.ToBase64String(hmac.ComputeHash([]));
        }

        using var response = await HttpClient.PostAsJsonAsync(webhookUrl, payload, cancellationToken);
        var error = await GetApiErrorAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return error is null
                ? NotificationSendResult.Failed(NotificationResultCode.HttpError, (int)response.StatusCode)
                : NotificationSendResult.Failed(NotificationResultCode.RemoteError, error);
        }

        return error is null
            ? NotificationSendResult.Succeeded(NotificationResultCode.FeishuSent)
            : NotificationSendResult.Failed(NotificationResultCode.RemoteError, error);
    }

    private static async Task<NotificationSendResult> SendEmailAsync(
        NotificationChannelSettings channel,
        NotificationMessage message,
        CancellationToken cancellationToken)
    {
        var host = Require(channel.SmtpHost, NotificationResultCode.SmtpHostRequired);
        var from = Require(
            string.IsNullOrWhiteSpace(channel.MailFrom) ? channel.SmtpUsername : channel.MailFrom,
            NotificationResultCode.MailFromRequired);
        var recipients = channel.MailTo
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (recipients.Length == 0)
        {
            throw new NotificationException(NotificationResultCode.MailToRequired);
        }

        var mail = new MimeMessage();
        mail.From.Add(MailboxAddress.Parse(from));
        foreach (var recipient in recipients)
        {
            mail.To.Add(MailboxAddress.Parse(recipient));
        }
        mail.Subject = message.Title;
        mail.Body = new TextPart("plain") { Text = message.Body };

        using var client = new SmtpClient();
        var socketOptions = !channel.SmtpUseSsl
            ? SecureSocketOptions.None
            : channel.SmtpPort == 465
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;
        await client.ConnectAsync(host, channel.SmtpPort, socketOptions, cancellationToken);
        if (!string.IsNullOrWhiteSpace(channel.SmtpUsername))
        {
            await client.AuthenticateAsync(channel.SmtpUsername, channel.SmtpPassword, cancellationToken);
        }

        await client.SendAsync(mail, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
        return NotificationSendResult.Succeeded(NotificationResultCode.EmailSent);
    }

    private static async Task<NotificationSendResult> SendTelegramAsync(
        NotificationChannelSettings channel,
        NotificationMessage message,
        CancellationToken cancellationToken)
    {
        var botToken = Require(channel.TelegramBotToken, NotificationResultCode.TelegramBotTokenRequired);
        var chatId = Require(channel.TelegramChatId, NotificationResultCode.TelegramChatIdRequired);
        var endpoint = $"https://api.telegram.org/bot{Uri.EscapeDataString(botToken)}/sendMessage";
        var text = $"{message.Title}\n\n{message.Body}";
        if (text.Length > 4096)
        {
            text = text[..4096];
        }

        using var response = await HttpClient.PostAsJsonAsync(endpoint, new
        {
            chat_id = chatId,
            text
        }, cancellationToken);
        var error = await GetApiErrorAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return error is null
                ? NotificationSendResult.Failed(NotificationResultCode.HttpError, (int)response.StatusCode)
                : NotificationSendResult.Failed(NotificationResultCode.RemoteError, error);
        }

        return error is null
            ? NotificationSendResult.Succeeded(NotificationResultCode.TelegramSent)
            : NotificationSendResult.Failed(NotificationResultCode.RemoteError, error);
    }

    private static async Task<string?> GetApiErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
            {
                return root.TryGetProperty("description", out var description)
                    ? description.GetString()
                    : null;
            }

            if (root.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.GetInt32() != 0)
            {
                return root.TryGetProperty("message", out var message)
                    ? message.GetString()
                    : null;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Require(string? value, NotificationResultCode code) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new NotificationException(code)
            : value.Trim();

    private static NotificationSendResult GetSafeErrorResult(Exception exception) => exception switch
    {
        FormatException => NotificationSendResult.Failed(NotificationResultCode.AddressOrAccountInvalid),
        HttpRequestException => NotificationSendResult.Failed(NotificationResultCode.NetworkFailed),
        AuthenticationException => NotificationSendResult.Failed(NotificationResultCode.AuthenticationFailed),
        _ when string.IsNullOrWhiteSpace(exception.Message) =>
            NotificationSendResult.Failed(NotificationResultCode.SendFailed),
        _ => new NotificationSendResult(false, exception.Message, NotificationResultCode.SendFailed)
    };

    private static HttpClient CreateHttpClient() => new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };
}
