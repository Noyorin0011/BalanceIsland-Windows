using System.Xml.Linq;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace BalanceIsland.Windows;

public enum NotificationDeliveryResult
{
    // The native platform accepted this Toast; system policy may still suppress display.
    NativeToast,
    // The caller must make its single fallback attempt without reopening the alert transition.
    Failed
}

public enum NotificationChannelStatus
{
    WindowsImportantNotification,
    WindowsNotification,
    TrayFallback,
    Unavailable
}

public sealed record BalanceNotification(
    BalanceAlertKind Kind,
    string Title,
    string Message,
    string AccountNote,
    string MaskedKeySuffix);

public sealed record AppNotification(string Title, string Message);

public static class AccountContextFormatter
{
    public static string Format(string? accountNote, string? maskedKeySuffix)
    {
        var note = accountNote?.Trim() ?? "";
        var safeMask = maskedKeySuffix is { Length: 8 } value &&
                       value.StartsWith(ApiKeySanitizer.IrreversiblePlaceholder, StringComparison.Ordinal)
            ? value
            : ApiKeySanitizer.IrreversiblePlaceholder;
        return note.Length == 0 ? safeMask : $"{note} · {safeMask}";
    }
}

public interface INotificationService : IDisposable
{
    NotificationChannelStatus ChannelStatus { get; }
    NotificationDeliveryResult Send(BalanceNotification notification);
    NotificationDeliveryResult Send(AppNotification notification);
    NotificationDeliveryResult SendTest();
}

public static class ToastPayloadBuilder
{
    public static string Build(BalanceNotification notification, bool urgentSupported)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var toast = new XElement("toast",
            urgentSupported ? new XAttribute("scenario", "urgent") : null,
            new XElement("visual",
                new XElement("binding", new XAttribute("template", "ToastGeneric"),
                    new XElement("text", "Balance Island"),
                    new XElement("text", $"{KindLabel(notification.Kind)} · {notification.Title}"),
                    new XElement("text", $"{AccountContextFormatter.Format(notification.AccountNote, notification.MaskedKeySuffix)} · {notification.Message}"))));
        return toast.ToString(SaveOptions.DisableFormatting);
    }

    public static string Build(AppNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var toast = new XElement("toast",
            new XElement("visual",
                new XElement("binding", new XAttribute("template", "ToastGeneric"),
                    new XElement("text", "Balance Island"),
                    new XElement("text", notification.Title),
                    new XElement("text", notification.Message))));
        return toast.ToString(SaveOptions.DisableFormatting);
    }

    private static string KindLabel(BalanceAlertKind kind) => kind switch
    {
        BalanceAlertKind.Warning15 => "余额预警",
        BalanceAlertKind.Critical => "余额临界",
        BalanceAlertKind.Anomaly => "异常变动",
        _ => "余额通知"
    };
}

public sealed class WindowsNotificationService : INotificationService
{
    private readonly DesktopNotificationIdentityResult _identity;
    private bool _disposed;

    public WindowsNotificationService()
    {
        _identity = DesktopNotificationIdentity.TryRegister();
        ChannelStatus = !_identity.IsRegistered
            ? NotificationChannelStatus.Unavailable
            : SupportsUrgentNotifications
                ? NotificationChannelStatus.WindowsImportantNotification
                : NotificationChannelStatus.WindowsNotification;
    }

    public NotificationChannelStatus ChannelStatus { get; private set; }

    public NotificationDeliveryResult Send(BalanceNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return SendXml(
            ToastPayloadBuilder.Build(notification, SupportsUrgentNotifications),
            highPriority: true);
    }

    public NotificationDeliveryResult Send(AppNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return SendXml(ToastPayloadBuilder.Build(notification), highPriority: false);
    }

    private NotificationDeliveryResult SendXml(string xml, bool highPriority)
    {
        if (_disposed || !_identity.IsRegistered)
        {
            ChannelStatus = NotificationChannelStatus.Unavailable;
            return NotificationDeliveryResult.Failed;
        }

        try
        {
            var document = new XmlDocument();
            document.LoadXml(xml);
            var toast = new ToastNotification(document);
            if (highPriority) toast.Priority = ToastNotificationPriority.High;
            ToastNotificationManager.CreateToastNotifier(DesktopNotificationIdentity.AppUserModelId).Show(toast);
            ChannelStatus = SupportsUrgentNotifications
                ? NotificationChannelStatus.WindowsImportantNotification
                : NotificationChannelStatus.WindowsNotification;
            return NotificationDeliveryResult.NativeToast;
        }
        catch (Exception) when (OperatingSystem.IsWindows())
        {
            ChannelStatus = NotificationChannelStatus.Unavailable;
            return NotificationDeliveryResult.Failed;
        }
    }

    public NotificationDeliveryResult SendTest() => Send(new BalanceNotification(
        BalanceAlertKind.Critical,
        "Balance Island · 测试",
        "这是测试通知；Windows 通知设置仍然优先。",
        "本机",
        "••••测试"));

    public void Dispose() => _disposed = true;

    private static bool SupportsUrgentNotifications =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22546);
}
