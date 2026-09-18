using System.Globalization;
using System.Windows;
using OpenCVCameraTracking.Core;
using OpenCVCameraTracking.Core.Notifications;

namespace OpenCVCameraTracking.Localization;

public static class LocalizationManager
{
    private const string DictionaryPrefix = "Languages/Strings.";

    public static string CurrentLanguage { get; private set; } = "zh-CN";

    public static void Apply(string language)
    {
        CurrentLanguage = language is "en-US" ? "en-US" : "zh-CN";
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var oldDictionary = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains(DictionaryPrefix, StringComparison.OrdinalIgnoreCase) == true);
        if (oldDictionary is not null)
        {
            dictionaries.Remove(oldDictionary);
        }

        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"Languages/Strings.{CurrentLanguage}.xaml", UriKind.Relative)
        });

        var culture = CultureInfo.GetCultureInfo(CurrentLanguage);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static string Get(string key) =>
        Application.Current?.TryFindResource(key) as string ?? key;

    public static string GetOrOriginal(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Application.Current?.TryFindResource(value) as string ?? value;

    public static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    public static string GetExceptionMessage(Exception exception)
    {
        if (exception is CoreException coreException)
        {
            return Format(GetCoreErrorResourceKey(coreException.Code), coreException.Arguments);
        }

        if (exception is NotificationException notificationException)
        {
            return GetNotificationResultMessage(
                NotificationSendResult.Failed(notificationException.Code, notificationException.Arguments));
        }

        if (exception is AggregateException aggregate && aggregate.InnerExceptions.Count > 0)
        {
            return GetExceptionMessage(aggregate.InnerExceptions[0]);
        }

        return exception.Message;
    }

    public static string GetNotificationResultMessage(NotificationSendResult result)
    {
        if (result.Code == NotificationResultCode.None)
        {
            return result.Detail ?? string.Empty;
        }

        if (result.Code == NotificationResultCode.SendFailed && !string.IsNullOrWhiteSpace(result.Detail))
        {
            return Format("NotificationSendFailedDetail", result.Detail);
        }

        var resourceKey = GetNotificationResultResourceKey(result.Code);
        if (resourceKey is null)
        {
            return result.Detail ?? Get("NotificationSendFailed");
        }

        var arguments = result.Arguments;
        if ((arguments is null || arguments.Length == 0) && !string.IsNullOrWhiteSpace(result.Detail))
        {
            arguments = [result.Detail];
        }

        return arguments is null || arguments.Length == 0
            ? Get(resourceKey)
            : Format(resourceKey, arguments);
    }

    public static string GetNotificationEventName(string eventKey) =>
        Get(eventKey switch
        {
            NotificationEventCatalog.RestrictedZoneEntered => "NotificationEventRestrictedZone",
            NotificationEventCatalog.UnknownTargetDetected => "NotificationEventUnknownTarget",
            _ => eventKey
        });

    private static string GetCoreErrorResourceKey(CoreErrorCode code) => code switch
    {
        CoreErrorCode.CompositeDetectorRequired => "CompositeDetectorRequired",
        CoreErrorCode.RestrictedZoneInvalid => "RestrictedZoneInvalid",
        CoreErrorCode.EngineAlreadyRunning => "EngineAlreadyRunning",
        CoreErrorCode.SourceOpenCancelled => "SourceOpenCancelled",
        CoreErrorCode.SourceOpenFailed => "SourceOpenFailed",
        CoreErrorCode.SourceAddressRequired => "SourceAddressRequired",
        CoreErrorCode.RtspReaderFailed => "RtspReaderFailed",
        CoreErrorCode.HaarModelNotFound => "HaarModelNotFound",
        CoreErrorCode.HaarModelLoadFailed => "HaarModelLoadFailed",
        CoreErrorCode.YoloModelNotFound => "YoloModelNotFound",
        CoreErrorCode.YoloModelLoadFailed => "YoloModelLoadFailed",
        CoreErrorCode.YoloOutputDimensionsUnsupported => "YoloOutputDimensionsUnsupported",
        CoreErrorCode.YoloOutputShapeUnsupported => "YoloOutputShapeUnsupported",
        CoreErrorCode.YoloXModelNotFound => "YoloXModelNotFound",
        CoreErrorCode.YoloXModelLoadFailed => "YoloXModelLoadFailed",
        CoreErrorCode.YoloXOutputShapeUnexpected => "YoloXOutputShapeUnexpected",
        CoreErrorCode.YuNetModelNotFound => "YuNetModelNotFound",
        CoreErrorCode.YuNetModelLoadFailed => "YuNetModelLoadFailed",
        CoreErrorCode.SFaceModelNotFound => "SFaceModelNotFound",
        CoreErrorCode.SFaceModelLoadFailed => "SFaceModelLoadFailed",
        _ => "ProcessingError"
    };

    private static string? GetNotificationResultResourceKey(NotificationResultCode code) => code switch
    {
        NotificationResultCode.UnsupportedType => "NotificationUnsupportedType",
        NotificationResultCode.SendCanceled => "NotificationSendCanceled",
        NotificationResultCode.ServerChanKeyRequired => "NotificationServerChanKeyRequired",
        NotificationResultCode.ServerChanSc3Invalid => "NotificationServerChanSc3Invalid",
        NotificationResultCode.ServerChanSent => "NotificationServerChanSent",
        NotificationResultCode.FeishuWebhookRequired => "NotificationFeishuWebhookRequired",
        NotificationResultCode.FeishuSent => "NotificationFeishuSent",
        NotificationResultCode.SmtpHostRequired => "NotificationSmtpHostRequired",
        NotificationResultCode.MailFromRequired => "NotificationMailFromRequired",
        NotificationResultCode.MailToRequired => "NotificationMailToRequired",
        NotificationResultCode.EmailSent => "NotificationEmailSent",
        NotificationResultCode.TelegramBotTokenRequired => "NotificationTelegramBotTokenRequired",
        NotificationResultCode.TelegramChatIdRequired => "NotificationTelegramChatIdRequired",
        NotificationResultCode.TelegramSent => "NotificationTelegramSent",
        NotificationResultCode.RemoteError => "NotificationRemoteError",
        NotificationResultCode.HttpError => "NotificationHttpError",
        NotificationResultCode.AddressOrAccountInvalid => "NotificationAddressOrAccountInvalid",
        NotificationResultCode.NetworkFailed => "NotificationNetworkFailed",
        NotificationResultCode.AuthenticationFailed => "NotificationAuthenticationFailed",
        NotificationResultCode.SendFailed => "NotificationSendFailed",
        _ => null
    };
}
