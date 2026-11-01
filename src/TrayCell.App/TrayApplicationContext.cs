using TrayCell.Devices;
using TrayCell.Devices.Controller;
using TrayCell.Devices.Razer;
using TrayCell.Monitoring;

namespace TrayCell.App;

/// <summary>
/// Owns the single tray icon, the flyout panel and the poll timer.
/// Polling happens off the UI thread; the UI only touches ready snapshots.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(45);

    private readonly BatteryMonitor _monitor;
    private readonly RazerBatterySource _mouseSource = new();
    private readonly NotifyIcon _icon;
    private readonly BatteryFlyout _flyout;
    private readonly System.Windows.Forms.Timer _timer;
    private IReadOnlyList<DeviceSnapshot> _snapshots = [];
    private bool _polling;

    public TrayApplicationContext()
    {
        _monitor = new BatteryMonitor([_mouseSource, new ControllerBatterySource()]);
        _flyout = new BatteryFlyout(() => _ = RefreshAsync(), ExitThread);

        _icon = new NotifyIcon
        {
            Visible = true,
            Icon = BatteryIconRenderer.RenderAggregate([]),
            Text = "TrayCell - reading...",
            ContextMenuStrip = CreateMenu(),
        };

        _icon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                ToggleFlyout();
            }
        };

        _timer = new System.Windows.Forms.Timer { Interval = (int)PollInterval.TotalMilliseconds };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
    }

    private ContextMenuStrip CreateMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => RebuildMenu(menu);
        return menu;
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
            Apply(snapshots);
        }
        finally
        {
            _polling = false;
        }
    }

    private void Apply(IReadOnlyList<DeviceSnapshot> snapshots)
    {
        _snapshots = snapshots;

        var previous = _icon.Icon;
        _icon.Icon = BatteryIconRenderer.RenderAggregate(snapshots);
        _icon.Text = Truncate(BuildToolTip(snapshots), 63);
        previous?.Dispose();

        if (_flyout.Visible)
        {
            _flyout.Update(snapshots);
        }
    }

    private void ToggleFlyout()
    {
        if (_flyout.Visible)
        {
            _flyout.Hide();
            return;
        }

        _flyout.Update(_snapshots);
        _flyout.ShowNear(Cursor.Position);
    }

    private void RebuildMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();

        if (_snapshots.Count == 0)
        {
            menu.Items.Add(new ToolStripMenuItem("Reading devices...") { Enabled = false });
        }

        foreach (var snapshot in _snapshots)
        {
            var item = new ToolStripMenuItem(snapshot.ToolTipText()) { Enabled = false };
            if (snapshot.State != BatteryState.Ok && snapshot.Detail is { Length: > 0 } detail)
            {
                item.ToolTipText = detail;
            }

            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());

        var refresh = new ToolStripMenuItem("Refresh now");
        refresh.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(refresh);

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitThread();
        menu.Items.Add(exit);
    }

    /// <summary>Shows the shortest useful label per device: "Mouse 57%", "Controller 100%".</summary>
    private static string BuildToolTip(IReadOnlyList<DeviceSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return "TrayCell - reading...";
        }

        var parts = snapshots.Select(snapshot =>
        {
            var label = ShortLabel(snapshot.Name);
            var level = snapshot.Level is { } value ? $"{value}%" : "unavailable";
            var stale = snapshot.State == BatteryState.Stale ? " (stale)" : string.Empty;
            var charging = snapshot.IsCharging == true && snapshot.State == BatteryState.Ok ? " charging" : string.Empty;
            return $"{label} {level}{stale}{charging}";
        });

        return string.Join("   ", parts);
    }

    private static string ShortLabel(string name) =>
        name.Contains("Razer", StringComparison.OrdinalIgnoreCase) ? "Mouse"
        : name.Contains("8BitDo", StringComparison.OrdinalIgnoreCase) ? "Controller"
        : name;

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        _flyout.Dispose();
        _mouseSource.Dispose();
        base.ExitThreadCore();
    }
}
