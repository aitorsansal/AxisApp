namespace AxisApp;

/// <summary>Shared between AxisFirebaseMessagingService (builds/posts the notification) and
/// NotificationActionReceiver (updates/dismisses it after a tray action completes) — both need
/// the same channel id and notification id to target the same tray entry.</summary>
internal static class AxisNotifications
{
    public const string ChannelId = "axis_default";

    // Fixed id: a second push while one is still showing replaces it rather than stacking a
    // second system-tray entry — see AxisFirebaseMessagingService's own remarks.
    public const int NotificationId = 1001;
}
