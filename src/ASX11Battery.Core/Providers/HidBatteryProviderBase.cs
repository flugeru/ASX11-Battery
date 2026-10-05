using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ASX11Battery.Core.Abstractions;
using ASX11Battery.Core.Diagnostics;
using ASX11Battery.Core.Hid;

namespace ASX11Battery.Core.Providers;

/// <summary>
/// Base class for HID-based battery providers. Handles enumeration, session
/// management, read loops, and decoding. Derived types only need to implement
/// device matching and frame decoding.
/// </summary>
public abstract class HidBatteryProviderBase : IBatteryProvider
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Manufacturer { get; }
    public abstract ConnectionType DefaultConnection { get; }
    public abstract string ProtocolSummary { get; }
    protected abstract bool Matches(DeviceNodeInfo info);
    protected abstract int Rank(DeviceNodeInfo info);
    protected abstract int MaxOpenSessions { get; }
    protected abstract bool DetectsCharging(IReadOnlyList<DeviceNodeInfo> matched);
    protected abstract bool TryDecode(byte[] frame, int shift, out DecodeResult result);

    // When wired and receiver interfaces coexist, derived providers can keep
    // stale wireless reports from overriding the wired battery state.
    protected virtual bool ShouldDecode(DeviceNodeInfo info, bool wiredPresent) => true;

    // Providers may identify a wired transition from a decoded report rather
    // than from a product ID. This keeps interface/PID changes transparent to
    // the watch loop.
    protected virtual bool IsChargingReport(DecodeResult decode) => decode.Charging == true;

    public int ConsensusFrames { get; set; } = 1;

    public ProviderDiagnostics? Diagnostics { get; protected set; }

    public IAsyncEnumerable<DeviceSnapshot> WatchAsync(ProviderOptions options, CancellationToken ct)
    {
        return WatchAsyncCore(options, ct);
    }

    private async IAsyncEnumerable<DeviceSnapshot> WatchAsyncCore(ProviderOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var diag = new ProviderDiagnostics
        {
            ProviderId = Id,
            ProviderName = DisplayName,
            ProtocolSummary = ProtocolSummary,
        };
        Diagnostics = diag;

        var last = DisconnectedSnapshot();
        Logger.Info($"PROVIDER start id={Id} name=\"{DisplayName}\" options reenumMs={options.ReenumerateIntervalMs} timeoutMs={options.ReadTimeoutMs} consensus={options.ConsensusFrames}");

        int reenumerateIntervalMs = Math.Clamp(options.ReenumerateIntervalMs, 500, 30_000);
        int readTimeoutMs = Math.Clamp(options.ReadTimeoutMs, 100, 10_000);
        int consensusFrames = Math.Clamp(options.ConsensusFrames, 1, 5);
        bool detectCharging = options.DetectCharging;
        int consecutiveReadIterations = 0;
        const int KeepAliveMs = 10000;

        while (!ct.IsCancellationRequested)
        {
            var all = HidInspector.Enumerate(forceRefresh: true);
            var matched = all.Where(Matches).OrderByDescending(Rank).ToList();
            diag.CapturedAt = DateTimeOffset.Now;
            diag.DevicePresent = matched.Count > 0;
            var selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            diag.Interfaces = matched.Select(m => ToDiagnostic(m, selectedPaths)).ToList();

            if (matched.Count == 0)
            {
                Logger.Event($"provider.{Id}.state", $"PROVIDER disconnected id={Id}");
                diag.Status = "Nenhuma interface HID correspondente encontrada.";
                diag.RecentFrames = new List<FrameDiagnostic>();
                diag.Note($"Enumeração: nenhum dispositivo {Manufacturer} presente.");

                var disconnected = last with
                {
                    State = DeviceState.Disconnected,
                    BatteryPercent = null,
                    Charging = null,
                    LastUpdate = null,
                    StatusMessage = "Desconectado",
                    ProtocolNote = null,
                    Connection = DefaultConnection,
                };
                if (!SameAs(last, disconnected))
                {
                    last = disconnected;
                    yield return last;
                }

                if (!await DelayAsync(reenumerateIntervalMs, ct).ConfigureAwait(false))
                    yield break;
                continue;
            }

            var connection = ResolveConnection(matched);
            bool wiredPresent = DetectsCharging(matched);
            Logger.Event($"provider.{Id}.connection", $"PROVIDER connected id={Id} connection={connection}");

            var sessions = new List<OpenSession>();
            foreach (var info in matched.Take(MaxOpenSessions))
            {
                if (!info.IsOpenable) continue;

                var session = HidSession.TryOpen(info.DevicePath);
                if (session is null)
                {
                    Logger.Event($"provider.{Id}.open.{info.ProductId:X4}", $"PROVIDER session open failed id={Id} pid=0x{info.ProductId:X4}", TimeSpan.FromMinutes(1));
                    continue;
                }

                selectedPaths.Add(info.DevicePath);
                sessions.Add(new OpenSession(session, info));
            }
            diag.Interfaces = matched.Select(m => ToDiagnostic(m, selectedPaths)).ToList();

            if (sessions.Count == 0)
            {
                Logger.Event($"provider.{Id}.state", $"PROVIDER battery unavailable id={Id} connection={connection} reason=no-open-sessions");
                diag.Status = "Dispositivo presente, mas nenhuma interface pôde ser aberta.";
                diag.Note("Todas as interfaces falharam ao abrir (provável bloqueio de driver).");

                var unavailable = last with
                {
                    State = DeviceState.BatteryUnavailable,
                    BatteryPercent = null,
                    Charging = null,
                    StatusMessage = "Detectado, mas as interfaces HID não puderam ser abertas",
                    ProtocolNote = null,
                    Connection = connection,
                };
                if (!SameAs(last, unavailable))
                {
                    last = unavailable;
                    yield return last;
                }

                if (!await DelayAsync(reenumerateIntervalMs, ct).ConfigureAwait(false))
                    yield break;
                continue;
            }

            foreach (var s in sessions) s.Session.Start();

            var frames = new List<FrameDiagnostic>();
            DateTimeOffset? lastYield = null;
            string? detectedModel = null;
            // Wired mode can replace the receiver's HID node before its first
            // battery frame arrives. Keep the last confirmed level meanwhile.
            int? percent = last.State == DeviceState.Connected ? last.BatteryPercent : null;
            bool? charging = null;
            string? protocolNote = null;
            int? candidatePercent = null;
            bool? candidateCharging = null;
            int candidateFrames = 0;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // HID handles can remain readable after USB removal. Re-enumerate
                    // while sessions are active so wired insertion/removal replaces
                    // stale handles instead of waiting for a read timeout/fault.
                    var currentPaths = HidInspector.Enumerate(forceRefresh: true)
                        .Where(Matches)
                        .Select(m => m.DevicePath)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (sessions.Any(s => !currentPaths.Contains(s.Info.DevicePath)))
                    {
                        Logger.Event($"provider.{Id}.interfaces", $"PROVIDER HID interfaces changed id={Id}");
                        break;
                    }

                    bool gotAnyFrame = false;
                    bool decodedSomething = false;

                    // Phase 1: drain whatever is already queued
                    var pending = new List<(OpenSession Session, byte[] Frame)>(sessions.Count);
                    foreach (var s in sessions)
                    {
                        if (s.Session.TryTakeFrame(out var frame) && frame.Length > 0)
                            pending.Add((s, frame));
                    }

                    // Phase 2: only when everything was silent, wait once for any of them
                    if (pending.Count == 0 && sessions.Count > 0 && !ct.IsCancellationRequested)
                    {
                        using var sweep = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        var waits = sessions
                            .Select(s => s.Session.WaitForFrameAsync(readTimeoutMs, sweep.Token).AsTask())
                            .ToArray();

                        try { await Task.WhenAny(waits).ConfigureAwait(false); }
                        catch (OperationCanceledException) { }

                        for (int i = 0; i < waits.Length; i++)
                        {
                            if (!waits[i].IsCompleted) continue;
                            var frame = await waits[i].ConfigureAwait(false);
                            if (frame.Length > 0) pending.Add((sessions[i], frame));
                        }

                        try { sweep.Cancel(); } catch (ObjectDisposedException) { }
                    }

                    foreach (var (s, frame) in pending)
                    {
                        if (ct.IsCancellationRequested) break;
                        gotAnyFrame = true;
                        int shift = s.Info.InputReportByteLength > 0 && frame.Length < s.Info.InputReportByteLength ? 1 : 0;
                        if (!ShouldDecode(s.Info, wiredPresent)) continue;

                        if (TryDecode(frame, shift, out var decode))
                        {
                            if (decode.Ok && decode.Percent is int p)
                            {
                                decodedSomething = true;
                                detectedModel = decode.Note ?? detectedModel;
                                protocolNote = decode.Note;

                                // The known FA60 report carries battery level only. Do not
                                // infer charging from any report byte.
                                charging = detectCharging ? (wiredPresent ? true : decode.Charging ?? false) : false;

                                // Require repeated equal reports only when configured. This
                                // filters an isolated corrupt packet without delaying default UI.
                                bool reportedCharging = detectCharging ? (wiredPresent ? true : decode.Charging ?? false) : false;
                                if (candidatePercent == p && candidateCharging == reportedCharging)
                                    candidateFrames++;
                                else
                                {
                                    candidatePercent = p;
                                    candidateCharging = reportedCharging;
                                    candidateFrames = 1;
                                }

                                if (candidateFrames >= consensusFrames)
                                {
                                    bool changed = percent != p || charging != reportedCharging;
                                    percent = p;
                                    charging = reportedCharging;
                                    if (changed)
                                    {
                                        frames.Add(new FrameDiagnostic
                                        {
                                            Timestamp = DateTimeOffset.Now,
                                            Data = frame,
                                            Verdict = "decoded",
                                            Percent = p,
                                            Charging = reportedCharging,
                                        });
                                    }
                                }
                            }
                            else if (frames.Count < 24)
                            {
                                frames.Add(new FrameDiagnostic
                                {
                                    Timestamp = DateTimeOffset.Now,
                                    Data = frame,
                                    Verdict = decode.Ok ? "decoded (no level)" : decode.Note ?? "unrecognized frame",
                                });
                            }
                        }
                        else if (frames.Count < 24)
                        {
                            frames.Add(new FrameDiagnostic
                            {
                                Timestamp = DateTimeOffset.Now,
                                Data = frame,
                                Verdict = "no signature match",
                            });
                        }
                    }

                    if (frames.Count > 24) frames.RemoveRange(0, frames.Count - 24);
                    diag.RecentFrames = frames.ToList();

                    if (decodedSomething || gotAnyFrame) consecutiveReadIterations = 0;
                    else consecutiveReadIterations++;

                    var now = DateTimeOffset.Now;
                    bool valueChanged = !SameAs(last, BuildSnapshot(
                        SnapshotName(detectedModel), connection, percent is null ? DeviceState.BatteryUnavailable : DeviceState.Connected,
                        percent, charging, now, percent is null ? StatusForUnavailable(wiredPresent) : null, protocolNote));

                    bool keepAliveDue = lastYield is null || (now - lastYield.Value).TotalMilliseconds >= KeepAliveMs;

                    if (valueChanged || keepAliveDue)
                    {
                        lastYield = now;
                        var snapshot = BuildSnapshot(
                            SnapshotName(detectedModel), connection,
                            percent is null ? DeviceState.BatteryUnavailable : DeviceState.Connected,
                            percent, charging, now, percent is null ? StatusForUnavailable(wiredPresent) : null, protocolNote);
                        Logger.Info($"PROVIDER snapshot id={Id} state={snapshot.State} connection={snapshot.Connection} percent={snapshot.BatteryPercent?.ToString() ?? "null"} charging={snapshot.Charging?.ToString() ?? "null"} status=\"{snapshot.StatusMessage}\"");
                        last = snapshot;
                        yield return snapshot;
                    }

                    diag.Status = percent is int
                        ? $"Bateria {percent}% lida com sucesso."
                        : StatusForUnavailable(wiredPresent);

                    if (sessions.Any(s => s.Session.IsFaulted)) break;

                    if (consecutiveReadIterations > 20)
                    {
                        diag.Note("Sem quadros por vários ciclos; re-enumerando interfaces.");
                        break;
                    }

                    try
                    {
                        await Task.Delay(Math.Max(120, readTimeoutMs / 2), ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                }
            }
            finally
            {
                HidInspector.InvalidateEnumerationCache();
                foreach (var s in sessions)
                {
                    s.Session.Dispose();
                    Logger.Info($"PROVIDER session closed id={Id} frames={s.Session.FramesRead} faulted={s.Session.IsFaulted} error=\"{s.Session.LastError}\" path=\"{s.Info.DevicePath}\"");
                    diag.Note($"Sessão encerrada após {s.Session.FramesRead} quadros.");
                }
                Logger.Info($"PROVIDER read cycle ended id={Id} lastState={last.State} connection={last.Connection}");
            }

            if (ct.IsCancellationRequested) yield break;
        }
    }

    private async Task<bool> DelayAsync(int ms, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;
        try { await Task.Delay(ms, ct).ConfigureAwait(false); return true; }
        catch (OperationCanceledException) { return false; }
    }

    private ConnectionType ResolveConnection(IReadOnlyList<DeviceNodeInfo> matched)
    {
        return matched.Any(m => m.ProductId == 0xFA55)
            ? ConnectionType.UsbWired
            : matched.Any(m => m.ProductId == 0xFA60)
                ? ConnectionType.Wireless24Ghz
                : DefaultConnection;
    }

    private string SnapshotName(string? model) =>
        string.IsNullOrWhiteSpace(model) ? DisplayName : $"{DisplayName} ({model})";

    private static string StatusForUnavailable(bool wiredPresent) =>
        wiredPresent ? "Conectado (carregando)" : "Bateria indisponível";

    private DeviceSnapshot DisconnectedSnapshot() => new(
        Id, DisplayName, Manufacturer, DisplayName, "mouse", DefaultConnection,
        false, DeviceState.Disconnected, null, null, null, "Desconectado", null);

    private DeviceSnapshot BuildSnapshot(
        string displayName, ConnectionType connection, DeviceState state,
        int? percent, bool? charging, DateTimeOffset now, string? status, string? note) => new(
        Id, displayName, Manufacturer, displayName, "mouse", connection,
        charging == true, state, percent, charging, now, status, note);

    private static bool SameAs(DeviceSnapshot a, DeviceSnapshot b) =>
        a.Connection == b.Connection &&
        a.State == b.State &&
        a.BatteryPercent == b.BatteryPercent &&
        a.Charging == b.Charging;

    private static InterfaceInfo ToDiagnostic(DeviceNodeInfo info, HashSet<string> selected) => new()
    {
        Path = info.DevicePath,
        UsagePage = info.UsagePage,
        Usage = info.Usage,
        IsOpenable = info.IsOpenable,
        Note = selected.Contains(info.DevicePath) ? "opened" : null,
    };

    private readonly struct OpenSession(HidSession Session, DeviceNodeInfo Info)
    {
        public HidSession Session { get; } = Session;
        public DeviceNodeInfo Info { get; } = Info;
    }

    public readonly struct DecodeResult
    {
        public bool Ok { get; init; }
        public int? Percent { get; init; }
        public bool? Charging { get; init; }
        public string? Note { get; init; }
    }
}