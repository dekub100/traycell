using System.Drawing;
using System.Drawing.Drawing2D;
using TrayCell.Monitoring;

namespace TrayCell.App;

/// <summary>
/// Borderless panel shown from the tray icon: one card per device with a level bar,
/// plus refresh and exit. Clicking a row pins that device to the tray icon.
/// Styled after the dekub.eu design system: flat, square, 1px borders, violet accent.
/// Closes itself when it loses focus.
/// </summary>
internal sealed class BatteryFlyout : Form
{
    private const int Edge = 12;
    private const int RowHeight = 56;
    private const int FooterHeight = 5;
    private const int PanelWidth = 296;
    private const int CardInset = 6;

    private readonly Action _refresh;
    private readonly Action _exit;
    private readonly Action<string?> _pin;
    private readonly Font _nameFont = new("Segoe UI Semibold", 10f, FontStyle.Regular);
    private readonly Font _levelFont = new("Consolas", 14f, FontStyle.Bold);
    private readonly Font _noteFont = new("Consolas", 8.5f, FontStyle.Regular);
    private readonly Font _actionFont = new("Consolas", 9f, FontStyle.Regular);

    private readonly List<(Rectangle Bounds, string Device)> _rowBounds = [];

    private IReadOnlyList<DeviceSnapshot> _snapshots = [];
    private string? _pinnedDevice;
    private Rectangle _refreshBounds;
    private Rectangle _exitBounds;
    private int _hoveredRow = -1;
    private bool _hoverRefresh;
    private bool _hoverExit;

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
        BackColor = Theme.Background;
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

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.None;
        _rowBounds.Clear();

        using (var background = new SolidBrush(Theme.Background))
        {
            graphics.FillRectangle(background, ClientRectangle);
        }

        using (var border = new Pen(Theme.Border))
        {
            graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        var y = Edge;

        if (_snapshots.Count == 0)
        {
            TextRenderer.DrawText(graphics, "reading...", _noteFont,
                new Point(Edge, y), Theme.Muted);
        }

        foreach (var snapshot in _snapshots)
        {
            var card = new Rectangle(
                Edge - CardInset, y - CardInset,
                Width - ((Edge - CardInset) * 2), RowHeight - 6);

            var isPinned = string.Equals(snapshot.Name, _pinnedDevice, StringComparison.OrdinalIgnoreCase);
            var isHovered = _rowBounds.Count == _hoveredRow;

            _rowBounds.Add((card, snapshot.Name));
            DrawRow(graphics, snapshot, card, isPinned, isHovered);
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
        var hoveredRow = -1;
        for (var i = 0; i < _rowBounds.Count; i++)
        {
            if (_rowBounds[i].Bounds.Contains(e.Location))
            {
                hoveredRow = i;
                break;
            }
        }

        var hoverRefresh = _refreshBounds.Contains(e.Location);
        var hoverExit = _exitBounds.Contains(e.Location);

        if (hoveredRow != _hoveredRow || hoverRefresh != _hoverRefresh || hoverExit != _hoverExit)
        {
            _hoveredRow = hoveredRow;
            _hoverRefresh = hoverRefresh;
            _hoverExit = hoverExit;
            Invalidate();
        }

        Cursor = hoveredRow >= 0 || hoverRefresh || hoverExit ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hoveredRow != -1 || _hoverRefresh || _hoverExit)
        {
            _hoveredRow = -1;
            _hoverRefresh = false;
            _hoverExit = false;
            Invalidate();
        }

        base.OnMouseLeave(e);
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

    private void DrawRow(Graphics graphics, DeviceSnapshot snapshot, Rectangle card, bool isPinned, bool isHovered)
    {
        using (var fill = new SolidBrush(isPinned ? Theme.AccentSoft : Theme.Surface))
        {
            graphics.FillRectangle(fill, card);
        }

        var borderColor = isPinned ? Theme.Accent : isHovered ? Theme.BorderHover : Theme.Border;
        using (var border = new Pen(borderColor))
        {
            graphics.DrawRectangle(border, card.X, card.Y, card.Width - 1, card.Height - 1);
        }

        if (isPinned)
        {
            using var accent = new SolidBrush(Theme.Accent);
            graphics.FillRectangle(accent, card.X, card.Y, 2, card.Height);
        }

        var contentX = card.X + 14;
        var contentWidth = card.Width - 28;
        var textColor = snapshot.State == BatteryState.Off ? Theme.Muted : Theme.Text;

        TextRenderer.DrawText(graphics, snapshot.Name, _nameFont,
            new Rectangle(contentX, card.Y + 8, contentWidth - 58, 20),
            textColor,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

        var levelText = snapshot.Level is { } level ? $"{level}%" : "--";
        TextRenderer.DrawText(graphics, levelText, _levelFont,
            new Rectangle(card.Right - 14 - 58, card.Y + 5, 58, 24),
            snapshot.Level is null ? Theme.Muted : textColor,
            TextFormatFlags.Right | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

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
                new Point(contentX, card.Y + 27), Theme.Muted,
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        // No level means no bar: an empty track next to "not connected" reads as noise.
        if (snapshot.Level is not { } value || snapshot.State == BatteryState.Off)
        {
            return;
        }

        var bar = new Rectangle(contentX, card.Y + 36, contentWidth, 3);
        using (var track = new SolidBrush(Theme.Border))
        {
            graphics.FillRectangle(track, bar);
        }

        var filled = new Rectangle(bar.X, bar.Y, Math.Max(3, bar.Width * value / 100), bar.Height);
        using var brush = new SolidBrush(BarColor(snapshot));
        graphics.FillRectangle(brush, filled);
    }

    private void DrawFooter(Graphics graphics, int y)
    {
        _refreshBounds = new Rectangle(Edge - 4, y - 11, 56, 12);
        _exitBounds = new Rectangle(Width - Edge - 30, y - 11, 34, 12);

        TextRenderer.DrawText(graphics, "refresh", _actionFont, _refreshBounds,
            _hoverRefresh ? Theme.Accent : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

        TextRenderer.DrawText(graphics, "exit", _actionFont, _exitBounds,
            _hoverExit ? Theme.Text : Theme.Muted,
            TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }

    private static Color BarColor(DeviceSnapshot snapshot) => snapshot.State switch
    {
        BatteryState.Stale => Theme.Muted,
        BatteryState.Wired => Theme.Muted,
        _ => snapshot.Level switch
        {
            null => Theme.Muted,
            >= 50 => Theme.Green,
            >= 20 => Theme.Accent,
            _ => Theme.Red,
        },
    };

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
}
