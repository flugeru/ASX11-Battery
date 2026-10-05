using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ASX11Battery.Core.Abstractions;
using ASX11Battery.Core.Diagnostics;

namespace ASX11Battery.Core.Hid;

/// <summary>
/// Enumerates HID devices and probes their capabilities without keeping handles open.
/// </summary>
public static class HidInspector
{
    public const ushort GenericDesktopUsagePage = 0x01;
    public const ushort VendorUsagePage = 0xFF00;
    private const int HidPStatusSuccess = 0x00110000;

    private static readonly object _enumLock = new();
    private static List<DeviceNodeInfo>? _cached;
    private static DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    /// <summary>Invalidates the enumeration cache so the next call re-enumerates.</summary>
    public static void InvalidateEnumerationCache()
    {
        lock (_enumLock)
        {
            _cached = null;
            _cachedAt = DateTimeOffset.MinValue;
            Logger.Event("hid.enum.cache", "HID enumeration cache invalidated");
        }
    }

    /// <summary>Returns all currently present HID interfaces, using a short-lived cache.</summary>
    public static IReadOnlyList<DeviceNodeInfo> Enumerate(bool forceRefresh = false)
    {
        lock (_enumLock)
        {
            if (!forceRefresh && _cached is not null && DateTimeOffset.Now - _cachedAt < TimeSpan.FromSeconds(2))
            {
                return _cached;
            }

            var list = new List<DeviceNodeInfo>();

            try
            {
            var classGuid = HidNative.GUID_DEVINTERFACE_HID;
            var hDevInfo = HidNative.SetupDiGetClassDevsW(
                ref classGuid, IntPtr.Zero, IntPtr.Zero,
                HidNative.DIGCF_PRESENT | HidNative.DIGCF_DEVICEINTERFACE);

            if (hDevInfo == IntPtr.Zero || hDevInfo == new IntPtr(-1))
            {
                Logger.Error($"HID ENUM SetupDiGetClassDevs failed win32={Marshal.GetLastWin32Error()}");
                return list;
            }

            try
            {
                var ifaceData = new HidNative.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<HidNative.SP_DEVICE_INTERFACE_DATA>() };
                for (int i = 0; HidNative.SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref classGuid, i, ref ifaceData); i++)
                {
                    int requiredSize = 0;
                    HidNative.SetupDiGetDeviceInterfaceDetailW(
                        hDevInfo, ref ifaceData, IntPtr.Zero, 0, out requiredSize, IntPtr.Zero);
                    if (requiredSize <= 0 || requiredSize > 64 * 1024)
                        continue;

                    // SP_DEVICE_INTERFACE_DETAIL_DATA_W has a 4-byte cbSize field
                    // followed by a UTF-16 path. Allocate the exact required size;
                    // the old fixed 256-char struct silently lost long HID paths.
                    IntPtr detailBuffer = Marshal.AllocHGlobal(requiredSize);
                    var devInfo = new HidNative.SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<HidNative.SP_DEVINFO_DATA>() };
                    string path;
                    try
                    {
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                        if (!HidNative.SetupDiGetDeviceInterfaceDetailW(
                                hDevInfo, ref ifaceData, detailBuffer, requiredSize,
                                out _, ref devInfo))
                            continue;

                        path = Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, 4)) ?? string.Empty;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailBuffer);
                    }

                    if (path.Length == 0)
                        continue;
                    var node = new DeviceNodeInfo { DevicePath = path };
                    ParseVidPid(path, out ushort vendorId, out ushort productId);
                    node.VendorId = vendorId; // Path-derived fallback if HidD attributes are denied.
                    node.ProductId = productId;

                    // Registry properties
                    node.Product = GetStringProperty(hDevInfo, ref devInfo, HidNative.SPDRP_DEVICEDESC);
                    node.Manufacturer = GetStringProperty(hDevInfo, ref devInfo, HidNative.SPDRP_MFG);

                    // VID/PID/Version via HidD_GetAttributes. Query-only access is
                    // sufficient for attributes/caps and remains share-compatible
                    // with native mouse/keyboard collections claimed by Windows.
                    using var handle = HidNative.CreateFileW(
                        path, 0, HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
                        IntPtr.Zero, HidNative.OPEN_EXISTING, HidNative.FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
                    if (!handle.IsInvalid)
                    {
                        var attrs = new HidNative.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HidNative.HIDD_ATTRIBUTES>() };
                        if (HidNative.HidD_GetAttributes(handle, ref attrs))
                        {
                            node.VendorId = attrs.VendorID;
                            node.ProductId = attrs.ProductID;
                            node.Version = attrs.VersionNumber;
                        }

                        // Caps
                        if (HidNative.HidD_GetPreparsedData(handle, out var prep))
                        {
                            try
                            {
                                if (HidNative.HidP_GetCaps(prep, out var caps) == HidPStatusSuccess)
                                {
                                    node.UsagePage = caps.UsagePage;
                                    node.Usage = caps.Usage;
                                    node.InputReportByteLength = caps.InputReportByteLength;
                                    node.OutputReportByteLength = caps.OutputReportByteLength;
                                    node.FeatureReportByteLength = caps.FeatureReportByteLength;
                                }
                            }
                            finally { HidNative.HidD_FreePreparsedData(prep); }
                        }
                    }

                    node.IsOpenable = IsOpenable(node);
                    list.Add(node);
                }
            }
            finally
            {
                HidNative.SetupDiDestroyDeviceInfoList(hDevInfo);
            }

            _cached = list;
            _cachedAt = DateTimeOffset.Now;
            return list;
            }
            catch (Exception ex)
            {
                Logger.Error("HID ENUM failed", ex);
                return list;
            }
        }
    }

    private static void ParseVidPid(string path, out ushort vendorId, out ushort productId)
    {
        vendorId = 0;
        productId = 0;
        var match = System.Text.RegularExpressions.Regex.Match(
            path, @"vid_([0-9a-fA-F]{4}).*?pid_([0-9a-fA-F]{4})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return;
        ushort.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out vendorId);
        ushort.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.HexNumber, null, out productId);
    }

    private static string GetStringProperty(IntPtr hDevInfo, ref HidNative.SP_DEVINFO_DATA devInfo, int property)
    {
        if (!HidNative.SetupDiGetDeviceRegistryPropertyW(hDevInfo, ref devInfo, property, out _, IntPtr.Zero, 0, out int required))
        {
            if (Marshal.GetLastWin32Error() != 122) return string.Empty; // ERROR_INSUFFICIENT_BUFFER
        }
        if (required <= 0) return string.Empty;

        IntPtr buffer = Marshal.AllocHGlobal(required);
        try
        {
            if (HidNative.SetupDiGetDeviceRegistryPropertyW(hDevInfo, ref devInfo, property, out _, buffer, required, out _))
            {
                return Marshal.PtrToStringUni(buffer) ?? string.Empty;
            }
            return string.Empty;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static bool IsOpenable(DeviceNodeInfo node)
    {
        if (node.InputReportByteLength == 0) return false;
        if (node.VendorId == 0 && node.ProductId == 0) return false;
        return true;
    }

    /// <summary>Probes capabilities for a single device path without caching.</summary>
    public static HidSession.HidOpenResult ProbeCapabilities(string path)
    {
        var result = new HidSession.HidOpenResult { Success = false };
        Logger.Info($"HID PROBE start path=\"{path}\"");

        // Query-only access is sufficient for attributes/caps and works even when
        // the native mouse/keyboard collection is already claimed by Win32.
        using var handle = HidNative.CreateFileW(path, 0, HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE, IntPtr.Zero, HidNative.OPEN_EXISTING, HidNative.FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            Logger.Error($"HID PROBE CreateFileW failed win32={Marshal.GetLastWin32Error()} path=\"{path}\"");
            return result;
        }

        if (!HidNative.HidD_GetPreparsedData(handle, out var prep))
        {
            Logger.Error($"HID PROBE HidD_GetPreparsedData failed win32={Marshal.GetLastWin32Error()} path=\"{path}\"");
            return result;
        }

        try
        {
            if (HidNative.HidP_GetCaps(prep, out var caps) != HidPStatusSuccess)
            {
                Logger.Error($"HID PROBE HidP_GetCaps failed path=\"{path}\"");
                return result;
            }

            result.Success = true;
            result.UsagePage = caps.UsagePage;
            result.Usage = caps.Usage;
            result.InputReportByteLength = caps.InputReportByteLength;
            result.OutputReportByteLength = caps.OutputReportByteLength;
            result.FeatureReportByteLength = caps.FeatureReportByteLength;
        }
        finally
        {
            HidNative.HidD_FreePreparsedData(prep);
        }
        Logger.Info($"HID PROBE complete success={result.Success} usagePage=0x{result.UsagePage:X4} usage=0x{result.Usage:X4} in={result.InputReportByteLength} out={result.OutputReportByteLength} feature={result.FeatureReportByteLength} path=\"{path}\"");
        return result;
    }
}