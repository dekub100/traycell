using TrayCell.Devices;
using TrayCell.Devices.Controller;
using TrayCell.Devices.Razer;
using TrayCell.Monitoring;

namespace TrayCell.App;

/// <summary>
/// Owns the two tray icons, the poll timer and the context menu.
/// Polling happens off the UI thread; the UI only touches ready snapshots.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(45);

    private readonly BatteryMonitor _monitor;
    private readonly RazerBatterySource _mouseSource = new();
    private readonly NotifyIcon _mouseIcon;
    private readonly NotifyIcon _controllerIcon;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _timer;
    private IReadOnlyList<DeviceSnapshot> _snapshots = [];
    private bool _polling;

    public TrayApplicationContext()
    {
        _monitor = new BatteryMonitor([_mouseSource, new ControllerBatterySource()]);

        _menu = new ContextMenuStrip();
        _menu.Opening += (_, _) => RebuildMenu();

        _mouseIcon = CreateTrayIcon();
        _controllerIcon = CreateTrayIcon();

        _timer = new System.Windows.Forms.Timer { Interval = (int)PollInterval.TotalMilliseconds };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
    }

    private NotifyIcon CreateTrayIcon()
    {
        var icon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = BatteryIconRenderer.Render(new DeviceSnapshot("", null, null, BatteryState.Off, null, null)),
            Text = "TrayCell - reading...",
            Visible = true,
        };

        icon.DoubleClick += async (_, _) => await RefreshAsync();
        return icon;
    }

    private async Task RefreshAsync()
    {
        if (_polling)
        {
            return;
        }

        _polling = true;
        try
        {
            var snapshots = await Task.Run(_monitor.Poll);
            _snapshots = snapshots;
            Apply(snapshots);
        }
        finally
        {
            _polling = false;
        }
    }

    private void Apply(IReadOnlyList<DeviceSnapshot> snapshots)
    {
        var mouse = Find(snapshots, _mouseSource.Name);
        var controller = Find(snapshots, ControllerBatterySource.DefaultFriendlyName);

        if (mouse is not null)
        {
            Update(_mouseIcon, mouse);
        }

        if (controller is not null)
        {
            Update(_controllerIcon, controller);
        }
    }

    private static void Update(NotifyIcon notifyIcon, DeviceSnapshot snapshot)
    {
        var previous = notifyIcon.Icon;
        notifyIcon.Icon = BatteryIconRenderer.Render(snapshot);
        notifyIcon.Text = Truncate(snapshot.ToolTipText(), 63);
        previous?.Dispose();
    }

    private void RebuildMenu()
    {
        _menu.Items.Clear();

        if (_snapshots.Count == 0)
        {
            _menu.Items.Add(new ToolStripMenuItem("Reading devices...") { Enabled = false });
        }

        foreach (var snapshot in _snapshots)
        {
            var item = new ToolStripMenuItem(snapshot.ToolTipText()) { Enabled = false };
            if (snapshot.State != BatteryState.Ok && snapshot.Detail is { Length: > 0 } detail)
            {
                item.ToolTipText = detail;
            }

            _menu.Items.Add(item);
        }

        _menu.Items.Add(new ToolStripSeparator());

        var refresh = new ToolStripMenuItem("Refresh now");
        refresh.Click += async (_, _) => await RefreshAsync();
        _menu.Items.Add(refresh);

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitThread();
        _menu.Items.Add(exit);
    }

    private static DeviceSnapshot? Find(IReadOnlyList<DeviceSnapshot> snapshots, string name) =>
        snapshots.FirstOrDefault(snapshot => string.Equals(snapshot.Name, name, StringComparison.OrdinalIgnoreCase));

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 1)] + "...";

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _mouseIcon.Visible = false;
        _controllerIcon.Visible = false;
        _mouseIcon.Dispose();
        _controllerIcon.Dispose();
        _menu.Dispose();
        _mouseSource.Dispose();
        base.ExitThreadCore();
    }
}
