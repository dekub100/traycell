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
    private readonly AppSettings _settings;
    private readonly NotifyIcon _icon;
    private readonly BatteryFlyout _flyout;
    private readonly System.Windows.Forms.Timer _timer;
    private IReadOnlyList<DeviceSnapshot> _snapshots = [];
    private bool _polling;

    public TrayApplicationContext()
    {
        _settings = AppSettings.Load();
        _monitor = new BatteryMonitor([_mouseSource, new ControllerBatterySource()]);
        _flyout = new BatteryFlyout(() => _ = RefreshAsync(), ExitThread, PinDevice);

        _icon = new NotifyIcon
        {
            Visible = true,
            Icon = BatteryIconRenderer.RenderFor([], _settings.PinnedDevice),
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

        ShowIcon();
        _flyout.Update(snapshots, _settings.PinnedDevice);
    }

    /// <summary>Repaints the tray icon and tooltip from the current snapshots and pin.</summary>
    private void ShowIcon()
    {
        var previous = _icon.Icon;
        _icon.Icon = BatteryIconRenderer.RenderFor(_snapshots, _settings.PinnedDevice);
        _icon.Text = Truncate(BuildToolTip(_snapshots, _settings.PinnedDevice), 63);
        previous?.Dispose();
    }

    /// <summary>
    /// Pins a device to the tray icon. Passing null returns to automatic mode.
    /// Clicking the pinned device again in the flyout also unpins.
    /// </summary>
    private void PinDevice(string? deviceName)
    {
        var normalized = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName;

        if (string.Equals(_settings.PinnedDevice, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.PinnedDevice = normalized;
        _settings.Save();

        ShowIcon();
        _flyout.Update(_snapshots, _settings.PinnedDevice);
    }

    private void ToggleFlyout()
    {
        if (_flyout.Visible)
        {
            _flyout.Hide();
            return;
        }

        _flyout.Update(_snapshots, _settings.PinnedDevice);
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
        menu.Items.Add(BuildPinMenu());
        menu.Items.Add(new ToolStripSeparator());

        var refresh = new ToolStripMenuItem("Refresh now");
        refresh.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(refresh);

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitThread();
        menu.Items.Add(exit);
    }

    /// <summary>Submenu that chooses which device the single tray icon reports.</summary>
    private ToolStripMenuItem BuildPinMenu()
    {
        var pinMenu = new ToolStripMenuItem("Tray icon shows");
        pinMenu.DropDownItems.Add(PinItem("Automatic", null));

        foreach (var snapshot in _snapshots)
        {
            pinMenu.DropDownItems.Add(PinItem(ShortLabel(snapshot.Name), snapshot.Name));
        }

        return pinMenu;
    }

    private ToolStripMenuItem PinItem(string label, string? deviceName)
    {
        var item = new ToolStripMenuItem(label)
        {
            Checked = string.Equals(_settings.PinnedDevice, deviceName, StringComparison.OrdinalIgnoreCase),
        };

        item.Click += (_, _) => PinDevice(deviceName);
        return item;
    }

    /// <summary>Shows the shortest useful label per device: "Mouse 57%", "Controller 100%".</summary>
    private static string BuildToolTip(IReadOnlyList<DeviceSnapshot> snapshots, string? pinnedDevice)
    {
        if (snapshots.Count == 0)
        {
            return "TrayCell - reading...";
        }

        // The pinned device leads so its level survives tooltip truncation.
        var ordered = snapshots
            .OrderBy(snapshot => string.Equals(snapshot.Name, pinnedDevice, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Select(snapshot =>
            {
                var label = ShortLabel(snapshot.Name);
                var level = snapshot.Level is { } value ? $"{value}%" : "unavailable";
                var stale = snapshot.State == BatteryState.Stale ? " (stale)" : string.Empty;
                var charging = snapshot.IsCharging == true && snapshot.State == BatteryState.Ok ? " charging" : string.Empty;
                var pinned = string.Equals(snapshot.Name, pinnedDevice, StringComparison.OrdinalIgnoreCase) ? " [pinned]" : string.Empty;
                return $"{label} {level}{stale}{charging}{pinned}";
            });

        return string.Join("   ", ordered);
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
