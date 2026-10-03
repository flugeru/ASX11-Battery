using System.Collections.Generic;
using ASX11Battery.Core.Abstractions;
using ASX11Battery.Core.Providers;

namespace ASX11Battery.Core.Providers;

/// <summary>
/// Central registry of known battery providers. Currently only the Attack Shark X11.
/// </summary>
public static class ProviderRegistry
{
    public static IReadOnlyList<IBatteryProvider> CreateDefault() =>
        new List<IBatteryProvider> { new AttackSharkX11Provider() };
}