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
    private Icon? _appIcon;
    private string? _lastSignature;
    private bool _disposed;

    /// <summary>Raised when the user picks "Abrir" or double-clicks the tray icon.</summary>
    public event EventHandler? OpenRequested;

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
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        _icon.ContextMenuStrip = menu;

        _appIcon = LoadAppIcon();
        _icon.Icon = (Icon)_appIcon.Clone();
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "ASX11Battery.ico");
            if (!File.Exists(path))
                path = Path.Combine(AppContext.BaseDirectory, "ASX11Battery.ico");

            return new Icon(path);
        }
        catch
        {
            return SystemIcons.Application;
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
        _icon.Icon = (Icon)_appIcon!.Clone();
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
        _appIcon?.Dispose();
        _appIcon = null;
    }

}