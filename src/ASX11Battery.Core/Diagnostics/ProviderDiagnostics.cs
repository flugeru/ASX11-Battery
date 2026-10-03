namespace ASX11Battery.Core.Diagnostics;

/// <summary>
/// Diagnostics information for a provider, used by the UI.
/// </summary>
public sealed class ProviderDiagnostics
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string ProtocolSummary { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<InterfaceInfo> Interfaces { get; set; } = new();
    public List<FrameDiagnostic> RecentFrames { get; set; } = new();
    public DateTimeOffset CapturedAt { get; set; }
    public bool DevicePresent { get; set; }

    private readonly List<string> _notes = new();
    public IReadOnlyList<string> Notes => _notes;

    public void Note(string message) => _notes.Add($"{DateTimeOffset.Now:HH:mm:ss.fff} {message}");
}

/// <summary>
/// Information about a HID interface seen during enumeration.
/// </summary>
public sealed class InterfaceInfo
{
    public string Path { get; set; } = string.Empty;
    public ushort UsagePage { get; set; }
    public ushort Usage { get; set; }
    public bool IsOpenable { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// A captured HID frame with decoding verdict.
/// </summary>
public sealed class FrameDiagnostic
{
    public DateTimeOffset Timestamp { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public string Verdict { get; set; } = string.Empty;
    public int? Percent { get; set; }
    public bool? Charging { get; set; }
    public int? PercentOffset { get; set; }
    public int? ChargingOffset { get; set; }
}