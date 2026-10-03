using ASX11Battery.Core.Hid;
using Xunit;

namespace ASX11Battery.Tests;

/// <summary>
/// The two HID path parsers, against paths captured from a real machine. Both are
/// pure string handling over Windows-specific quirks, and the easiest place in
/// the codebase for a refactor to silently break device discovery.
/// </summary>
public sealed class HidPathTests
{
    // Captured from a live 1D57:FA60 adapter on Windows 11.
    private const string Wireless =
        @"\\?\hid#vid_1d57&pid_fa60&mi_02#8&4ad706a&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private const string Bluetooth =
        @"\\?\hid#vid_1d57&pid_fa60&mi_01&col03#8&17dd8ca8&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    [Fact]
    public void ParsesTheInterfaceIndexOutOfTheHardwareIdSegment()
    {
        // The index is not a path segment. It lives inside the hardware id, so
        // splitting on backslashes would never find it.
        Assert.Equal(2, HidInspector.ParseInterfaceNumber(Wireless));
        Assert.Equal(1, HidInspector.ParseInterfaceNumber(Bluetooth));
    }

    [Fact]
    public void ParsesTheIndexAsHexadecimal()
    {
        // "&mi_0a" is interface 10. Reading it as decimal would silently pick the
        // wrong collection whenever the device reaches ten.
        Assert.Equal(10, HidInspector.ParseInterfaceNumber(
            @"\\?\hid#vid_1d57&pid_fa60&mi_0a#8&17dd8ca8&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}"));
    }

    [Theory]
    [InlineData(null, -1)]
    [InlineData("", -1)]
    [InlineData(@"\\?\hid#vid_1d57&pid_fa60", -1)]
    [InlineData(@"\\?\hid#vid_1d57&pid_fa60&mi_XX#8&17dd8ca8", -1)]
    public void ReturnsMinusOneWhenThereIsNoInterfaceIndex(string? path, int expected)
    {
        // -1 rather than an exception: a missing index means "unknown", and the
        // caller already has a ranking that copes with it.
        Assert.Equal(expected, HidInspector.ParseInterfaceNumber(path));
    }

    [Fact]
    public void ExtractsTheDeviceInstanceIdWithoutTheInterfaceGuid()
    {
        var id = HidInspector.ExtractDeviceInstanceId(Wireless);

        Assert.NotNull(id);
        Assert.Equal("hid#vid_1d57&pid_fa60&mi_02#8&4ad706a&0&0002", id);
    }

    [Theory]
    [InlineData(@"\\?\hid#vid_1d57&pid_fa60&mi_02#8&4ad706a&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}")]
    [InlineData(@"\\.\hid#vid_1d57&pid_fa60&mi_02#8&4ad706a&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}")]
    [InlineData(@"hid#vid_1d57&pid_fa60&mi_02#8&4ad706a&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}")]
    public void StripsWhicheverDosDevicePrefixIsPresent(string path)
    {
        // SetupDi gives back the "\\?\" form, other sources hand out "\\.\" or
        // nothing at all. All three denote the same instance.
        var id = HidInspector.ExtractDeviceInstanceId(path);

        Assert.NotNull(id);
        Assert.Equal("hid#vid_1d57&pid_fa60&mi_02#8&4ad706a&0&0002", id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#{4d1e55b2-f16f-11cf-88cb-001111000030}")]
    public void ReturnsNullWhenNoInstanceIdCanBeRecovered(string? path)
    {
        Assert.Null(HidInspector.ExtractDeviceInstanceId(path));
    }

    [Fact]
    public void EveryVendorUsagePageIsRecognisedNotJustTheDocumentedOne()
    {
        // 0xFFF0 is a measured value, not a typo. Matching "equals 0xFF00" would
        // rank the wrong collection first and then read an endpoint that carries
        // no battery at all.
        Assert.True(HidInspector.IsVendorUsagePage(0xFF00));
        Assert.True(HidInspector.IsVendorUsagePage(0xFFF0));
        Assert.False(HidInspector.IsVendorUsagePage(0x000A));
        Assert.False(HidInspector.IsVendorUsagePage(0x000C));
    }

    [Fact]
    public void FormatsBytesForTheDiagnosticsReport()
    {
        Assert.Equal("03 55 40 01 5C", HidInspector.ToHex(new byte[] { 0x03, 0x55, 0x40, 0x01, 0x5C }));
    }
}
