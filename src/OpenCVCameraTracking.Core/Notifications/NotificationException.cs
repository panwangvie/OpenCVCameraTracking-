namespace OpenCVCameraTracking.Core.Notifications;

public enum NotificationResultCode
{
    None,
    UnsupportedType,
    SendCanceled,
    ServerChanKeyRequired,
    ServerChanSc3Invalid,
    ServerChanSent,
    FeishuWebhookRequired,
    FeishuSent,
    SmtpHostRequired,
    MailFromRequired,
    MailToRequired,
    EmailSent,
    TelegramBotTokenRequired,
    TelegramChatIdRequired,
    TelegramSent,
    RemoteError,
    HttpError,
    AddressOrAccountInvalid,
    NetworkFailed,
    AuthenticationFailed,
    SendFailed
}

public sealed class NotificationException : Exception
{
    public NotificationException(NotificationResultCode code, params object[] arguments)
        : base(code.ToString())
    {
        Code = code;
        Arguments = arguments;
    }

    public NotificationResultCode Code { get; }

    public object[] Arguments { get; }
}
