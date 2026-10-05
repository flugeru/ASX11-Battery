using System;
using System.Collections.Generic;
using System.Linq;
using ASX11Battery.Core.Abstractions;
using ASX11Battery.Core.Diagnostics;
using ASX11Battery.Core.Hid;

namespace ASX11Battery.Core.Providers;

/// <summary>
/// Battery provider for the Attack Shark X11 mouse.
/// </summary>
/// <remarks>
/// The X11 battery report is 03 55 40 01 XX, where XX is percentage. This
/// report carries no proven charging flag. Wired mode is identified by PID FA55;
/// wireless mode is PID FA60. The device is read-only.
/// </remarks>
public sealed class AttackSharkX11Provider : HidBatteryProviderBase
{
    public override string Id => "attack-shark-x11";
    public override string DisplayName => "Attack Shark X11";
    public override string Manufacturer => "Attack Shark";
    public override ConnectionType DefaultConnection => ConnectionType.Wireless24Ghz;
    public override string ProtocolSummary =>
        "USB receiver HID collection (MI_02, Col 03) emits 5-byte reports: " +
        "03 55 40 01 XX on battery or 03 55 40 03 XX while charging. " +
        "Byte 4 is battery percentage 0-100.";

    protected override bool Matches(DeviceNodeInfo info) =>
        // FA55 proves a physical wired connection even if its collection exposes
        // no readable input reports. FA60 must expose a usable report endpoint.
        (info.VendorId == 0x1D57 && info.ProductId == 0xFA55) ||
        (info.VendorId == 0x1D57 && info.ProductId == 0xFA60 &&
         info.InputReportByteLength >= 5) ||
        IsX11LikeFallback(info);

    private static bool IsX11LikeFallback(DeviceNodeInfo info)
    {
        // Keep discovery resilient to firmware/driver revisions that expose the
        // same receiver with missing attributes or a changed product ID. The
        // frame signature is still required before any battery value is accepted.
        bool vendorCollection = info.UsagePage >= 0xFF00;
        bool plausibleReport = info.InputReportByteLength >= 5 && info.InputReportByteLength <= 512;
        bool x11Path = info.DevicePath.Contains("vid_1d57", StringComparison.OrdinalIgnoreCase)
                    && info.DevicePath.Contains("pid_fa", StringComparison.OrdinalIgnoreCase);
        bool wiredPid = info.ProductId == 0xFA55 ||
                        info.DevicePath.Contains("pid_fa55", StringComparison.OrdinalIgnoreCase);
        return wiredPid || (plausibleReport && (vendorCollection || x11Path));
    }

    protected override int Rank(DeviceNodeInfo info)
    {
        // Battery report lives on receiver MI_02 Col 03. Keep it ahead of the
        // mouse/keyboard collections because MaxOpenSessions limits candidates.
        if (info.DevicePath.Contains("mi_02&col03", StringComparison.OrdinalIgnoreCase))
            return 200;

        return info.Usage switch
        {
            0x02 => 100, // Mouse
            0x06 => 50,  // Keyboard (MI_00/MI_03) - may have charging info
            _ => 10,
        };
    }

    protected override int MaxOpenSessions => 3;

    protected override bool DetectsCharging(IReadOnlyList<DeviceNodeInfo> matched) =>
        // FA55 is X11 wired mode. FA60 is the wireless receiver and can stay
        // enumerated while the cable is connected, so never use FA60 as proof
        // of charging.
        matched.Any(m => m.VendorId == 0x1D57 && m.ProductId == 0xFA55);

    protected override bool ShouldDecode(DeviceNodeInfo info, bool wiredPresent) =>
        // When wired (FA55), ignore the wireless receiver (FA60) to prevent
        // '01' (battery) frames from flickering the charging state.
        !wiredPresent || info.ProductId != 0xFA60;

    protected override bool TryDecode(byte[] frame, int shift, out DecodeResult result)
    {
        result = new DecodeResult { Ok = false };

        if (frame.Length < 5 + shift) return false;
        if (frame[0 + shift] != 0x03 || frame[1 + shift] != 0x55 || frame[2 + shift] != 0x40)
            return false;

        byte statusFlag = frame[3 + shift];
        if (statusFlag != 0x01 && statusFlag != 0x03)
            return false;

        int percent = frame[4 + shift];
        if (percent < 0 || percent > 100) return false;

        bool isCharging = statusFlag == 0x03;

        result = new DecodeResult
        {
            Ok = true,
            Percent = percent,
            Charging = isCharging,
            Note = isCharging ? "X11 charging report" : "X11 battery report"
        };
        return true;
    }

}