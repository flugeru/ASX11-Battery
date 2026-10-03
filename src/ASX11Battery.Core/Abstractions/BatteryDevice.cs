namespace ASX11Battery.Core.Abstractions;

/// <summary>
/// Represents the connection type of a battery device.
/// </summary>
public enum ConnectionType
{
    Unknown = 0,
    Wireless24Ghz = 1,
    Bluetooth = 2,
    UsbWired = 3,
}

/// <summary>
/// Current state of the device from the battery perspective.
/// </summary>
public enum DeviceState
{
    Disconnected = 0,
    Connected = 1,
    BatteryUnavailable = 2,
}

/// <summary>
/// Immutable snapshot of a device's battery status at a point in time.
/// </summary>
public sealed record DeviceSnapshot(
    string Id,
    string DisplayName,
    string Manufacturer,
    string Model,
    string IconKind,
    ConnectionType Connection,
    bool IsCharging,
    DeviceState State,
    int? BatteryPercent,
    bool? Charging,
    DateTimeOffset? LastUpdate,
    string? StatusMessage,
    string? ProtocolNote
);