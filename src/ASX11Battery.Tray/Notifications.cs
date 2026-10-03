using ASX11Battery.Core.Services;

namespace ASX11Battery.Tray;

/// <summary>
/// Bridges core notification requests to the shell, and holds the single
/// <see cref="TrayService"/> instance the view models talk to.
/// </summary>
public static class Notifications
{
    private static TrayService? _tray;

    public static void Attach(TrayService tray) => _tray = tray;

    public static void Detach(TrayService tray)
    {
        if (ReferenceEquals(_tray, tray))
            _tray = null;
    }

    public static void OnNotification(NotificationRequest request) =>
        _tray?.Notify(request.Title, request.Body, request.Kind);

    public static void Refresh(IReadOnlyList<(string Name, int? Percent, bool? Charging)> devices) =>
        _tray?.Refresh(devices);
}