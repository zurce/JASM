namespace GIMI_ModManager.WinUI.Services.Notifications;

using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.WinUI.Contracts.Services;

// This is a static class to easily  launch a not implemented notification from different places in the app.
internal static class NotImplemented
{
    public static NotificationManager NotificationManager { get; set; } = null!;

    public static void Show(string? message = null, TimeSpan? time = null)
    {
        NotificationManager.ShowNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("Notification_NotImplemented") ?? "Not Implemented", message ?? App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("Notification_NotImplementedMsg") ?? "This feature is not implemented yet.",
            time ?? TimeSpan.FromSeconds(2));
    }
}