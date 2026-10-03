using System.Drawing;
using System.Windows.Forms;
using ASX11Battery.Core.Services;

namespace ASX11Battery.Tray;

/// <summary>
/// Owns the notification-area icon and the balloon notifications.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private Icon? _current;
    private string? _lastSignature;
    private bool _disposed;

    /// <summary>Raised when the user picks "Abrir" or double-clicks the tray icon.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Raised when the user picks "Configurações".</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised when the user picks "Sair".</summary>
    public event EventHandler? ExitRequested;

    public TrayService()
    {
        _icon = new NotifyIcon
        {
            Text = "ASX11 Battery — iniciando…",
            Visible = true,
        };

        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _icon.MouseUp += OnMouseUp;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir ASX11 Battery", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Configurações", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        _icon.ContextMenuStrip = menu;

        Swap(LoadAppIcon());
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            // TrayService is in a separate assembly, so resolve the copied icon
            // relative to the executable rather than the current working folder.
            string path = Path.Combine(AppContext.BaseDirectory, "ASX11Battery.ico");
            return new Icon(path, 32, 32);
        }
        catch
        {
            return CreateTrayIcon(null, false);
        }
    }

    public void Notify(string title, string body, NotificationKind kind)
    {
        if (_disposed) return;

        ToolTipIcon icon = kind switch
        {
            NotificationKind.Warning => ToolTipIcon.Warning,
            NotificationKind.Charging => ToolTipIcon.Info,
            _ => ToolTipIcon.None,
        };

        _icon.BalloonTipTitle = Trim(title, 63);
        _icon.BalloonTipText = Trim(body, 220);
        _icon.BalloonTipIcon = icon;
        _icon.ShowBalloonTip(kind == NotificationKind.Warning ? 8000 : 4000);
    }

    /// <summary>Updates the tray icon and tooltip from the current device list.</summary>
    public void Refresh(IReadOnlyList<(string Name, int? Percent, bool? Charging)> devices)
    {
        if (_disposed) return;

        var connected = devices.Where(d => d.Percent is not null || d.Charging is not null).ToList();
        int? lowest = connected.Count == 0
            ? null
            : connected.Where(d => d.Percent is int).Select(d => d.Percent!.Value).DefaultIfEmpty(0).Min();

        bool charging = connected.Any(d => d.Charging == true);

        string signature = $"{lowest?.ToString() ?? "-"}:{(charging ? 1 : 0)}";
        if (signature == _lastSignature) return;

        _lastSignature = signature;
        Swap(CreateTrayIcon(lowest, charging));
        _icon.Text = BuildTooltip(devices);
    }

    private static string BuildTooltip(IReadOnlyList<(string Name, int? Percent, bool? Charging)> devices)
    {
        var lines = new List<string> { "ASX11 Battery" };

        foreach (var (name, percent, charging) in devices)
        {
            string level = percent is int p ? $"{p}%" : "bateria indisponível";
            string charge = charging == true ? " · carregando" : string.Empty;
            lines.Add($"{name}: {level}{charge}");
        }

        string text = string.Join(" | ", lines);
        return Trim(text, 63);
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    private void Swap(Icon next)
    {
        var previous = _current;
        _current = next;
        _icon.Icon = next;
        previous?.Dispose();
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
            return;

        if (e.Button == MouseButtons.Left)
            OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _current?.Dispose();
        _current = null;
    }

    /// <summary>Creates a tray icon with the given battery level and charging state.</summary>
    private static Icon CreateTrayIcon(int? percent, bool charging)
    {
        using var bmp = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);

        var rect = new Rectangle(2, 2, 28, 28);
        using var path = RoundedRect(rect, 6);
        using var backgroundBrush = new SolidBrush(charging ? Color.FromArgb(255, 61, 220, 151) : Color.FromArgb(255, 26, 31, 39));
        g.FillPath(backgroundBrush, path);

        using var pen = new Pen(Color.FromArgb(255, 61, 220, 151), 2);
        g.DrawPath(pen, path);

        // Battery fill
        if (percent is int p)
        {
            int fillWidth = (int)Math.Max(1, 24 * (p / 100.0));
            var fillRect = new Rectangle(4, 4, fillWidth, 24);
            using var fillPath = RoundedRect(fillRect, 4);
            using var fillBrush = new SolidBrush(GetLevelColor(p));
            g.FillPath(fillBrush, fillPath);
        }
        else
        {
            // Unknown - draw a question mark
            using var font = new Font("Segoe UI", 14, FontStyle.Bold);
            using var brush = new SolidBrush(Color.FromArgb(255, 107, 114, 128));
            g.DrawString("?", font, brush, 8, 5);
        }

        // Charging bolt
        if (charging)
        {
            using var boltPen = new Pen(Color.White, 2);
            g.DrawLine(boltPen, 18, 8, 22, 16);
            g.DrawLine(boltPen, 22, 16, 17, 16);
            g.DrawLine(boltPen, 17, 16, 21, 24);
        }

        return Icon.FromHandle(bmp.GetHicon());
    }

    private static Color GetLevelColor(int percent) => percent switch
    {
        <= 10 => Color.FromArgb(255, 255, 92, 108),
        <= 25 => Color.FromArgb(255, 255, 180, 84),
        <= 75 => Color.FromArgb(255, 61, 220, 151),
        _ => Color.FromArgb(255, 69, 217, 232),
    };

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}