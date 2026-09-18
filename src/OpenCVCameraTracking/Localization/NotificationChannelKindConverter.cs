using System.Globalization;
using System.Windows.Data;
using OpenCVCameraTracking.Core.Notifications;

namespace OpenCVCameraTracking.Localization;

public sealed class NotificationChannelKindConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is NotificationChannelKind kind
            ? LocalizationManager.Get(kind switch
            {
                NotificationChannelKind.ServerChan => "NotificationTypeServerChan",
                NotificationChannelKind.Feishu => "NotificationTypeFeishu",
                NotificationChannelKind.Email => "NotificationTypeEmail",
                NotificationChannelKind.Telegram => "NotificationTypeTelegram",
                _ => "NotificationDefaultName"
            })
            : value?.ToString() ?? string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
