using System;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;

namespace ASX11Battery.Core.Hid;

/// <summary>
/// An open HID interface plus a background read pump.
/// </summary>
/// <remarks>
/// The read pump keeps exactly one blocking ReadFile in flight on a dedicated
/// thread and pushes completed frames into a bounded channel. When no report is
/// arriving that thread is parked inside the kernel waiting on the USB interrupt
/// endpoint, so an idle device costs effectively zero CPU and zero wakeups.
/// Polling is a non-blocking drain of that channel rather than a repeated
/// syscall storm against the device.
/// </remarks>
public sealed class HidSession : IDisposable
{
    private readonly string _path;
    private readonly int _readBufferSize;
    private readonly Channel<byte[]> _frames;
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private SafeFileHandle? _handle;
    private Thread? _readPump;
    private IntPtr _readPumpThread;
    private long _framesRead;
    private string? _lastError;
    private bool _disposed;

    public string Path => _path;
    public ushort InputReportByteLength { get; }
    public ushort OutputReportByteLength { get; }
    public ushort FeatureReportByteLength { get; }
    public ushort UsagePage { get; }
    public ushort Usage { get; }
    public long FramesRead => Interlocked.Read(ref _framesRead);
    public string? LastError => _lastError;
    public bool IsFaulted { get; private set; }

    private HidSession(string path, HidOpenResult caps)
    {
        _path = path;
        UsagePage = caps.UsagePage;
        Usage = caps.Usage;
        InputReportByteLength = caps.InputReportByteLength;
        OutputReportByteLength = caps.OutputReportByteLength;
        FeatureReportByteLength = caps.FeatureReportByteLength;
        _readBufferSize = Math.Max((int)caps.InputReportByteLength, 32) + 16;

        _frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = true,
        });
    }

    /// <summary>Attempts to open a HID interface at the given device path.</summary>
    public static HidSession? TryOpen(string path)
    {
        var caps = HidInspector.ProbeCapabilities(path);
        if (!caps.Success)
            return null;

        var session = new HidSession(path, caps);

        var handle = HidNative.CreateFileW(
            path, HidNative.GENERIC_READ,
            HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
            IntPtr.Zero, HidNative.OPEN_EXISTING,
            HidNative.FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);

        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        session._handle = handle;
        return session;
    }

    /// <summary>Starts the background read pump.</summary>
    public void Start()
    {
        if (_handle is null)
            throw new ObjectDisposedException(nameof(HidSession));

        _readPump = new Thread(() => ReadPump(_stopping.Token))
        {
            Name = "hid-read",
            IsBackground = true,
        };
        _readPump.Start();
    }

    /// <summary>Non-blocking drain of the frame queue.</summary>
    public bool TryTakeFrame(out byte[] frame)
    {
        if (_frames.Reader.TryRead(out var existing))
        {
            frame = existing;
            return true;
        }
        frame = Array.Empty<byte>();
        return false;
    }

    /// <summary>
    /// Waits up to timeoutMs for a frame, without blocking a thread.
    /// </summary>
    public async ValueTask<byte[]> WaitForFrameAsync(int timeoutMs, CancellationToken ct)
    {
        if (timeoutMs <= 0)
            return Array.Empty<byte>();

        using var timeout = new CancellationTokenSource(timeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, _stopping.Token, ct);

        try
        {
            if (await _frames.Reader.WaitToReadAsync(linked.Token).ConfigureAwait(false) &&
                _frames.Reader.TryRead(out var frame))
            {
                return frame;
            }
        }
        catch (OperationCanceledException) { }
        catch (ChannelClosedException) { }

        return Array.Empty<byte>();
    }

    private void ReadPump(CancellationToken ct)
    {
        var handle = _handle;
        if (handle is null) return;

        _readPumpThread = HidNative.GetCurrentThread();

        var buffer = new byte[_readBufferSize];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                bool ok = HidNative.ReadFile(handle, buffer, (uint)buffer.Length, out uint read, IntPtr.Zero);

                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == HidNative.ERROR_OPERATION_ABORTED || ct.IsCancellationRequested)
                        break;

                    _lastError = $"ReadFile failed (Win32 {err})";

                    if (err is 5 or 6 or 32 or 1165)
                    {
                        IsFaulted = true;
                        break;
                    }

                    Thread.Sleep(100);
                    continue;
                }

                if (read == 0) continue;

                var frame = new byte[read];
                Buffer.BlockCopy(buffer, 0, frame, 0, (int)read);
                _frames.Writer.TryWrite(frame);
                Interlocked.Increment(ref _framesRead);
            }
        }
        catch (Exception ex)
        {
            _lastError = $"read pump crashed: {ex.GetType().Name}: {ex.Message}";
            IsFaulted = true;
        }
        finally
        {
            _frames.Writer.TryComplete();
            _readPumpThread = IntPtr.Zero;
            _closed.TrySetResult();
        }
    }

    /// <summary>Waits briefly for the read pump to unwind. Used before reopening a device.</summary>
    public bool WaitForClose(int millisecondsTimeout) => _closed.Task.Wait(millisecondsTimeout);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _stopping.Cancel(); } catch { }

        var thread = Interlocked.Exchange(ref _readPumpThread, IntPtr.Zero);
        if (thread != IntPtr.Zero)
            HidNative.CancelSynchronousIo(thread);

        try { _readPump?.Join(1000); } catch { }

        _handle?.Dispose();
        _handle = null;
        _stopping.Dispose();
    }

    /// <summary>Result of probing a device's capabilities before opening.</summary>
    public struct HidOpenResult
    {
        public bool Success { get; set; }
        public ushort UsagePage { get; set; }
        public ushort Usage { get; set; }
        public ushort InputReportByteLength { get; set; }
        public ushort OutputReportByteLength { get; set; }
        public ushort FeatureReportByteLength { get; set; }
    }
}