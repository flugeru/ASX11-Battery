using System.Collections.Generic;
using ASX11Battery.Core.Abstractions;
using ASX11Battery.Core.Diagnostics;

namespace ASX11Battery.Core.Abstractions;

/// <summary>
/// Contract for a battery provider. Implementations read a specific device's
/// battery over HID (or other transport) and yield snapshots.
/// </summary>
public interface IBatteryProvider
{
    /// <summary>Stable identifier for the provider (e.g. "attack-shark-x11").</summary>
    string Id { get; }

    /// <summary>Human-readable display name.</summary>
    string DisplayName { get; }

    /// <summary>Manufacturer name.</summary>
    string Manufacturer { get; }

    /// <summary>Default connection type for this provider.</summary>
    ConnectionType DefaultConnection { get; }

    /// <summary>How many consecutive matching frames are required before a reading is accepted.</summary>
    int ConsensusFrames { get; set; }

    /// <summary>
    /// Human-readable summary of the protocol/parsing logic, used in diagnostics.
    /// </summary>
    string ProtocolSummary { get; }

    /// <summary>
    /// Current diagnostics for this provider, if any.
    /// </summary>
    ProviderDiagnostics? Diagnostics { get; }

    /// <summary>
    /// Starts the watch loop for this provider, yielding snapshots as they arrive.
    /// </summary>
    /// <param name="options">Provider options (polling, consensus, etc.).</param>
    /// <param name="ct">Cancellation token to stop the watch loop.</param>
    /// <returns>Async stream of device snapshots.</returns>
    IAsyncEnumerable<DeviceSnapshot> WatchAsync(ProviderOptions options, CancellationToken ct);
}

/// <summary>
/// Runtime options passed to a provider's watch loop.
/// </summary>
public sealed class ProviderOptions
{
    /// <summary>Milliseconds to wait for a HID report before considering the device silent.</summary>
    public int ReadTimeoutMs { get; set; } = 700;

    /// <summary>Milliseconds between re-enumeration attempts when no device is found.</summary>
    public int ReenumerateIntervalMs { get; set; } = 3000;

    /// <summary>
    /// Number of matching reports required before a new battery value replaces the
    /// displayed value. One preserves immediate device feedback.
    /// </summary>
    public int ConsensusFrames { get; set; } = 1;
}