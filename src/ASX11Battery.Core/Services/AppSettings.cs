using System.Text.Json.Serialization;
using ASX11Battery.Core.Abstractions;

namespace ASX11Battery.Core.Services;

/// <summary>
/// User-configurable application settings. Stored as JSON in the user's AppData.
/// </summary>
public sealed class AppSettings
{
    // Theme is fixed to Dark in the app. Kept only for backward-compatible JSON loading.
    public string Theme { get; set; } = "Dark";
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public bool CloseToTray { get; set; } = true;
    public int LowBatteryThreshold { get; set; } = 20;
    public bool NotifyLowBattery { get; set; } = true;
    public bool NotifyCharging { get; set; } = true;
    public bool ShowDisconnected { get; set; } = true;

    public ProviderOptions ProviderOptions { get; set; } = new();
}