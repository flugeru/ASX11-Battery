using ASX11Battery.Core.Hid;
using ASX11Battery.Core.Providers;
using Xunit;

namespace ASX11Battery.Tests;

public sealed class AttackSharkProviderTests
{
    [Theory]
    [InlineData(0x01, false)]
    [InlineData(0x03, true)]
    public void DecodesBatteryAndChargingFlags(byte flag, bool charging)
    {
        var provider = new AttackSharkX11Provider();
        var frame = new byte[] { 0x03, 0x55, 0x40, flag, 64 };

        var method = typeof(AttackSharkX11Provider).GetMethod(
            "TryDecode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var args = new object[] { frame, 0, null! };
        Assert.True((bool)method.Invoke(provider, args)!);
        var result = (HidBatteryProviderBase.DecodeResult)args[2];
        Assert.True(result.Ok);
        Assert.Equal(64, result.Percent);
        Assert.Equal(charging, result.Charging);
    }

    [Fact]
    public void RejectsUnknownStatusFlag()
    {
        var provider = new AttackSharkX11Provider();
        var frame = new byte[] { 0x03, 0x55, 0x40, 0x02, 64 };
        var method = typeof(AttackSharkX11Provider).GetMethod(
            "TryDecode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var args = new object[] { frame, 0, null! };
        Assert.False((bool)method.Invoke(provider, args)!);
    }

    [Fact]
    public void SnapshotComparisonIncludesConnectionType()
    {
        var type = typeof(HidBatteryProviderBase);
        var method = type.GetMethod("SameAs", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var wireless = new ASX11Battery.Core.Abstractions.DeviceSnapshot(
            "x11", "X11", "Attack Shark", "X11", "mouse",
            ASX11Battery.Core.Abstractions.ConnectionType.Wireless24Ghz, false,
            ASX11Battery.Core.Abstractions.DeviceState.Connected, 64, false,
            null, null, null);
        var wired = wireless with { Connection = ASX11Battery.Core.Abstractions.ConnectionType.UsbWired };

        Assert.False((bool)method.Invoke(null, new object[] { wireless, wired })!);
    }
}
