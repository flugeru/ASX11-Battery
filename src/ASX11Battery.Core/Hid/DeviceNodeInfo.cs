using System.Collections.Generic;

namespace ASX11Battery.Core.Hid;

/// <summary>
/// Metadata for a HID device node (interface/collection), obtained from the registry.
/// </summary>
public sealed class DeviceNodeInfo
{
    public string DevicePath { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public ushort VendorId { get; set; }
    public ushort ProductId { get; set; }
    public ushort Version { get; set; }
    public ushort UsagePage { get; set; }
    public ushort Usage { get; set; }
    public int InputReportByteLength { get; set; }
    public int OutputReportByteLength { get; set; }
    public int FeatureReportByteLength { get; set; }
    public string Manufacturer { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public bool IsOpenable { get; set; }
}