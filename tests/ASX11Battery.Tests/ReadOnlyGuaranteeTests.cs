using System.Reflection;
using ASX11Battery.Core.Hid;
using Xunit;

namespace ASX11Battery.Tests;

/// <summary>
/// Guards the project's central promise: this application cannot write to a
/// device.
/// </summary>
/// <remarks>
/// The X11 streams its battery report passively, so the absence of a write path
/// is a safety property rather than a missing feature. These are reflection tests
/// on purpose: there is no compile-time way to assert that an entire API surface
/// is absent, and a test that fails the moment a <c>WriteFile</c> import reappears
/// is the only thing that actually holds the line.
/// </remarks>
public sealed class ReadOnlyGuaranteeTests
{
    [Fact]
    public void TheInteropSurfaceHasNoWriteFileImport()
    {
        // HidNative is internal; reached by name so the test compiles even if the
        // type is renamed, and fails with a clear message if it is deleted.
        var type = typeof(HidInspector).Assembly
            .GetType("ASX11Battery.Core.Hid.HidNative", throwOnError: true)!;

        var writes = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => string.Equals(m.Name, "WriteFile", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            writes.Count == 0,
            "HidNative must not import WriteFile: the X11 needs no write, and a " +
            "blocking HID output transfer can hang forever on a device that is not " +
            "consuming reports.");
    }

    [Fact]
    public void TheInteropSurfaceNeverAsksForWriteAccess()
    {
        // Only an access-granting flag matters here. FILE_SHARE_WRITE is the
        // opposite: it permits *other* processes to keep writing, and the session
        // must pass it so the mouse driver retains ownership of the device.
        // Confusing the two would lock the user's mouse out.
        var type = typeof(HidInspector).Assembly
            .GetType("ASX11Battery.Core.Hid.HidNative", throwOnError: true)!;

        var granting = type
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f.Name.StartsWith("GENERIC_", StringComparison.Ordinal)
                     || f.Name.Contains("GENERICWRITE", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Name)
            .ToList();

        Assert.DoesNotContain("GENERIC_WRITE", granting);
    }

    [Fact]
    public void SessionsAreOpenedWithoutAnyAccessFlags()
    {
        // TryOpen takes only the path: there is nothing to opt out of, because
        // there is nothing to opt into.
        var open = typeof(HidSession).GetMethod(
            nameof(HidSession.TryOpen),
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(open);
        Assert.Equal(new[] { typeof(string) }, open!.GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public void ASessionExposesNoWayToSendAFrame()
    {
        // Outgoing report length is capability information, not a send path, and
        // the probe prints it when diagnosing a device. Everything with Write or
        // Send in its name is a method this application must not have.
        var members = typeof(HidSession)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.MemberType != MemberTypes.Constructor)
            .Where(m => m.Name.Contains("Write", StringComparison.OrdinalIgnoreCase)
                     || m.Name.Contains("Send", StringComparison.OrdinalIgnoreCase)
                     || m.Name.Contains("Transmit", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Name)
            .ToList();

        Assert.True(
            members.Count == 0,
            $"HidSession exposes write-capable members: {string.Join(", ", members)}.");
    }

    [Fact]
    public void NoUserSettingCanTurnWritesOn()
    {
        // A setting that would re-enable writes must never reappear unnoticed.
        var settings = typeof(ASX11Battery.Core.Services.AppSettings);

        var properties = settings
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.Contains("Write", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToList();

        Assert.True(
            properties.Count == 0,
            $"AppSettings exposes write-related properties: {string.Join(", ", properties)}.");
    }

    [Fact]
    public void TheProviderRegistryContainsOnlyTheMouse()
    {
        var registry = ASX11Battery.Core.Providers.ProviderRegistry.CreateDefault();

        var ids = registry.Select(p => p.Id).ToList();

        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.Contains("x11", id, StringComparison.OrdinalIgnoreCase));

        // IconKind lives on the HID base class, not the interface, so the check
        // goes through the concrete type. The point stands either way: nothing in
        // the registry may be anything other than the mouse.
        Assert.All(registry, p => Assert.Equal(
            "mouse",
            Assert.IsAssignableFrom<ASX11Battery.Core.Providers.HidBatteryProviderBase>(p).IconKind));
    }
}
