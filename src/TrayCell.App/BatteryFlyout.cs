using System.Drawing;
using System.Drawing.Drawing2D;
using TrayCell.Monitoring;

namespace TrayCell.App;

/// <summary>
/// Borderless panel shown from the tray icon: one row per device with a level bar,
/// plus Refresh and Exit. Clicking a row pins that device to the tray icon.
/// Closes itself when it loses focus.
/// </summary>
internal sealed class BatteryFlyout : Form
{
    private const int Edge = 16;
    private const int RowHeight = 58;
    private const int FooterHeight = 40;
    private const int PanelWidth = 320;

    private readonly Action _refresh;
    private readonly Action _exit;
    private readonly Action<string?> _pin;
    private readonly Font _nameFont = new("Segoe UI", 9.5f, FontStyle.Regular);
    private readonly Font _levelFont = new("Segoe UI", 15f, FontStyle.Bold);
    private readonly Font _noteFont = new("Segoe UI", 8f, FontStyle.Regular);
    private readonly Font _actionFont = new("Segoe UI", 9f, FontStyle.Regular);

    private readonly List<(Rectangle Bounds, string Device)> _rowBounds = [];

    private IReadOnlyList<DeviceSnapshot> _snapshots = [];
    private string? _pinnedDevice;
    private Rectangle _refreshBounds;
    private Rectangle _exitBounds;

    public BatteryFlyout(Action refresh, Action exit, Action<string?> pin)
    {
        _refresh = refresh;
        _exit = exit;
        _pin = pin;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(28, 28, 30);
        ClientSize = new Size(PanelWidth, Edge + RowHeight + FooterHeight);
    }

    public void Update(IReadOnlyList<DeviceSnapshot> snapshots, string? pinnedDevice)
    {
        _snapshots = snapshots;
        _pinnedDevice = pinnedDevice;
        ClientSize = new Size(PanelWidth, Edge + RowHeight * Math.Max(1, snapshots.Count) + FooterHeight);
        Invalidate();
    }

    /// <summary>Places the panel above the point (the tray cursor), clamped to the screen.</summary>
    public void ShowNear(Point anchor)
    {
        var screen = Screen.FromPoint(anchor).WorkingArea;

        var x = Math.Clamp(anchor.X - Width + 40, screen.Left + 8, screen.Right - Width - 8);
        var y = anchor.Y - Height - 12;
        if (y < screen.Top + 8)
        {
            y = anchor.Y + 12;
        }

        Location = new Point(x, y);
        Show();
        Activate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);

        var bounds = new Rectangle(0, 0, Width, Height);
        using var path = Rounded(bounds, 10);
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        _rowBounds.Clear();

        using (var background = new SolidBrush(BackColor))
        {
            graphics.FillRectangle(background, ClientRectangle);
        }

        var y = Edge;

        if (_snapshots.Count == 0)
        {
            TextRenderer.DrawText(graphics, "Reading devices...", _nameFont,
                new Point(Edge, y), Color.Gainsboro);
        }

        foreach (var snapshot in _snapshots)
        {
            var rowBounds = new Rectangle(Edge - 8, y - 8, Width - ((Edge - 8) * 2), RowHeight - 6);
            _rowBounds.Add((rowBounds, snapshot.Name));
            DrawRow(graphics, snapshot, y, rowBounds);
            y += RowHeight;
        }

        DrawFooter(graphics, Height - FooterHeight);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Hide();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Hide();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var interactive = _refreshBounds.Contains(e.Location)
                          || _exitBounds.Contains(e.Location)
                          || _rowBounds.Any(row => row.Bounds.Contains(e.Location));

        Cursor = interactive ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (_refreshBounds.Contains(e.Location))
            {
                _refresh();
            }
            else if (_exitBounds.Contains(e.Location))
            {
                _exit();
            }
            else
            {
                foreach (var (bounds, device) in _rowBounds)
                {
                    if (!bounds.Contains(e.Location))
                    {
                        continue;
                    }

                    // Clicking the pinned device again returns to automatic mode.
                    _pin(string.Equals(device, _pinnedDevice, StringComparison.OrdinalIgnoreCase) ? null : device);
                    break;
                }
            }
        }

        base.OnMouseClick(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _nameFont.Dispose();
            _levelFont.Dispose();
            _noteFont.Dispose();
            _actionFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void DrawRow(Graphics graphics, DeviceSnapshot snapshot, int y, Rectangle rowBounds)
    {
        var isPinned = string.Equals(snapshot.Name, _pinnedDevice, StringComparison.OrdinalIgnoreCase);

        if (isPinned)
        {
            using var highlight = new SolidBrush(Color.FromArgb(40, 40, 45));
            using var highlightPath = Rounded(rowBounds, 6);
            graphics.FillPath(highlight, highlightPath);

            var accent = new Rectangle(rowBounds.X + 2, rowBounds.Y + 8, 3, rowBounds.Height - 16);
            using var accentBrush = new SolidBrush(Color.FromArgb(122, 178, 255));
            using var accentPath = Rounded(accent, 1);
            graphics.FillPath(accentBrush, accentPath);
        }

        var textColor = snapshot.State == BatteryState.Off
            ? Color.FromArgb(150, 150, 150)
            : Color.FromArgb(238, 238, 238);

        TextRenderer.DrawText(graphics, snapshot.Name, _nameFont,
            new Rectangle(Edge, y, Width - (Edge * 2) - 70, 20),
            textColor, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var levelText = snapshot.Level is { } level ? $"{level}%" : "--";
        TextRenderer.DrawText(graphics, levelText, _levelFont,
            new Rectangle(Width - Edge - 76, y - 6, 76, 30),
            textColor, TextFormatFlags.Right | TextFormatFlags.NoPrefix);

        var note = snapshot.State switch
        {
            BatteryState.Stale => $"stale, last seen {Age(snapshot.LastUpdated)}",
            BatteryState.Off => "not connected",
            BatteryState.Wired => "connected over USB, no battery level",
            _ => snapshot.IsCharging == true ? "charging" : string.Empty,
        };

        if (note.Length > 0)
        {
            TextRenderer.DrawText(graphics, note, _noteFont,
                new Point(Edge, y + 20), Color.FromArgb(148, 148, 148));
        }

        var bar = new Rectangle(Edge, y + 40, Width - (Edge * 2), 6);
        using (var track = new SolidBrush(Color.FromArgb(58, 58, 62)))
        using (var trackPath = Rounded(bar, 3))
        {
            graphics.FillPath(track, trackPath);
        }

        if (snapshot.Level is { } value && snapshot.State != BatteryState.Off)
        {
            var fill = new Rectangle(bar.X, bar.Y, Math.Max(4, bar.Width * value / 100), bar.Height);
            using var brush = new SolidBrush(BarColor(snapshot));
            using var fillPath = Rounded(fill, 3);
            graphics.FillPath(brush, fillPath);
        }
    }

    private void DrawFooter(Graphics graphics, int y)
    {
        using (var separator = new Pen(Color.FromArgb(52, 52, 56)))
        {
            graphics.DrawLine(separator, Edge, y, Width - Edge, y);
        }

        _refreshBounds = new Rectangle(Edge - 6, y + 8, 70, 24);
        _exitBounds = new Rectangle(Width - Edge - 48, y + 8, 54, 24);

        TextRenderer.DrawText(graphics, "Refresh", _actionFont, _refreshBounds,
            Color.FromArgb(122, 178, 255),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(graphics, "Exit", _actionFont, _exitBounds,
            Color.FromArgb(158, 158, 158),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private static Color BarColor(DeviceSnapshot snapshot)
    {
        if (snapshot.State == BatteryState.Stale)
        {
            return Color.FromArgb(126, 126, 132);
        }

        return snapshot.Level switch
        {
            null => Color.FromArgb(126, 126, 132),
            >= 50 => Color.FromArgb(0, 200, 83),
            >= 20 => Color.FromArgb(255, 179, 0),
            _ => Color.FromArgb(244, 67, 54),
        };
    }

    private static string Age(DateTimeOffset? lastUpdated)
    {
        if (lastUpdated is not { } seen)
        {
            return "unknown";
        }

        var age = DateTimeOffset.Now - seen;
        return age.TotalMinutes < 1
            ? $"{(int)age.TotalSeconds}s ago"
            : $"{(int)age.TotalMinutes}m ago";
    }

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}
