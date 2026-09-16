namespace OpenCVCameraTracking.Core.Notifications;

public sealed record NotificationEventDefinition(string Key, string DisplayNameResourceKey);

public static class NotificationEventCatalog
{
    public const string RestrictedZoneEntered = "RestrictedZoneEntered";
    public const string UnknownTargetDetected = "UnknownTargetDetected";

    public static IReadOnlyList<NotificationEventDefinition> Definitions { get; } =
    [
        new(RestrictedZoneEntered, "NotificationEventRestrictedZone"),
        new(UnknownTargetDetected, "NotificationEventUnknownTarget")
    ];

    public static IReadOnlyList<string> DefaultKeys =>
        Definitions.Select(definition => definition.Key).ToArray();
}

public enum NotificationChannelKind
{
    ServerChan,
    Feishu,
    Email,
    Telegram
}

public sealed class NotificationChannelSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public NotificationChannelKind Kind { get; set; } = NotificationChannelKind.ServerChan;
    public List<string> EventKeys { get; set; } = NotificationEventCatalog.DefaultKeys.ToList();

    public string ServerChanSendKey { get; set; } = string.Empty;

    public string FeishuWebhookUrl { get; set; } = string.Empty;
    public string FeishuSecret { get; set; } = string.Empty;

    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;
    public string SmtpUsername { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public string MailFrom { get; set; } = string.Empty;
    public string MailTo { get; set; } = string.Empty;

    public string TelegramBotToken { get; set; } = string.Empty;
    public string TelegramChatId { get; set; } = string.Empty;

    public NotificationChannelSettings DeepClone() => new()
    {
        Id = Id,
        Name = Name,
        Enabled = Enabled,
        Kind = Kind,
        EventKeys = EventKeys?.ToList() ?? [],
        ServerChanSendKey = ServerChanSendKey,
        FeishuWebhookUrl = FeishuWebhookUrl,
        FeishuSecret = FeishuSecret,
        SmtpHost = SmtpHost,
        SmtpPort = SmtpPort,
        SmtpUseSsl = SmtpUseSsl,
        SmtpUsername = SmtpUsername,
        SmtpPassword = SmtpPassword,
        MailFrom = MailFrom,
        MailTo = MailTo,
        TelegramBotToken = TelegramBotToken,
        TelegramChatId = TelegramChatId
    };
}

public sealed record NotificationMessage(string Title, string Body);

public sealed record NotificationSendResult(bool Success, string Detail)
{
    public static NotificationSendResult Succeeded(string detail) => new(true, detail);
    public static NotificationSendResult Failed(string detail) => new(false, detail);
}

public sealed record NotificationDeliveryResult(
    NotificationChannelSettings Channel,
    NotificationSendResult Result);
