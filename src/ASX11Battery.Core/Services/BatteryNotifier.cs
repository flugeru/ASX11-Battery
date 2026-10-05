using ASX11Battery.Core.Abstractions;
using ASX11Battery.Core.Services;

namespace ASX11Battery.Core.Services;

/// <summary>
/// Notification request sent from the monitor to the UI layer.
/// </summary>
public sealed class NotificationRequest
{
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public NotificationKind Kind { get; init; }
}

/// <summary>
/// Type of notification for styling.
/// </summary>
public enum NotificationKind
{
    Info = 0,
    Warning = 1,
    Charging = 2,
}

/// <summary>
/// Evaluates device snapshots and raises notification requests when thresholds are crossed.
/// </summary>
public sealed class BatteryNotifier
{
    private readonly AppSettings _settings;
    private readonly HashSet<string> _lowNotified = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _chargingNotified = new(StringComparer.OrdinalIgnoreCase);

    public BatteryNotifier(AppSettings settings) => _settings = settings;

    public event EventHandler<NotificationRequest>? NotificationRaised;

    public void Evaluate(DeviceSnapshot snapshot)
    {
        if (!_settings.BatteryNotificationsEnabled || !_settings.NotifyLowBattery)
        {
            _lowNotified.Remove(snapshot.Id);
        }
        else if (snapshot.BatteryPercent is int p && p <= _settings.LowBatteryThreshold)
        {
            if (_lowNotified.Add(snapshot.Id))
            {
                Raise(new NotificationRequest
                {
                    Title = "Bateria baixa",
                    Body = $"{snapshot.DisplayName}: {p}% restante.",
                    Kind = NotificationKind.Warning,
                });
            }
        }
        else
        {
            _lowNotified.Remove(snapshot.Id);
        }

        if (!_settings.BatteryNotificationsEnabled || !_settings.NotifyCharging || !_settings.DetectCharging)
        {
            _chargingNotified.Remove(snapshot.Id);
        }
        else if (snapshot.Charging == true && _chargingNotified.Add(snapshot.Id))
        {
            Raise(new NotificationRequest
            {
                Title = "Carregando",
                Body = $"{snapshot.DisplayName} começou a carregar.",
                Kind = NotificationKind.Charging,
            });
        }
        else if (snapshot.Charging != true)
        {
            _chargingNotified.Remove(snapshot.Id);
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        if (!ReferenceEquals(_settings, settings))
            throw new ArgumentException("Notifier must use the shared settings instance.", nameof(settings));
    }

    public void ResetDevice(string id)
    {
        _lowNotified.Remove(id);
        _chargingNotified.Remove(id);
    }

    public void EvaluateBatteryCrossing(DeviceSnapshot previous, DeviceSnapshot current)
    {
        if (previous.BatteryPercent is int oldValue && current.BatteryPercent is int newValue &&
            oldValue > _settings.LowBatteryThreshold && newValue <= _settings.LowBatteryThreshold)
            Evaluate(current);
    }

    public bool ChargingDetectionEnabled => _settings.DetectCharging;

    private void Raise(NotificationRequest req) => NotificationRaised?.Invoke(this, req);

    public void Reset()
    {
        _lowNotified.Clear();
        _chargingNotified.Clear();
    }
}