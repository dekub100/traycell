using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using TrayCell.Monitoring;

namespace TrayCell.App;

/// <summary>Draws a small battery icon whose fill and colour encode the level.</summary>
internal static class BatteryIconRenderer
{
    private static readonly Color Good = Color.FromArgb(0, 200, 83);
    private static readonly Color Warn = Color.FromArgb(255, 179, 0);
    private static readonly Color Low = Color.FromArgb(244, 67, 54);
    private static readonly Color Unknown = Color.FromArgb(160, 160, 160);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon Render(DeviceSnapshot snapshot)
    {
        var level = snapshot.Level;
        var color = snapshot.State == BatteryState.Off || level is null
            ? Unknown
            : level >= 50 ? Good : level >= 20 ? Warn : Low;

        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        var body = new Rectangle(2, 8, 22, 16);
        using (var shell = new Pen(Color.FromArgb(240, 240, 240), 2f))
        using (var shellPath = RoundedRectangle(body, 3))
        {
            graphics.DrawPath(shell, shellPath);
            graphics.FillRectangle(shell.Brush, new Rectangle(25, 13, 3, 6));
        }

        if (level is { } value && value > 0)
        {
            var width = Math.Max(2, (int)Math.Round((body.Width - 4) * value / 100.0));
            using var fill = new SolidBrush(color);
            graphics.FillRectangle(fill, new Rectangle(body.X + 2, body.Y + 2, width, body.Height - 4));
        }

        if (snapshot.State == BatteryState.Stale)
        {
            using var slash = new Pen(Color.FromArgb(200, 255, 255, 255), 2f);
            graphics.DrawLine(slash, 4, 24, 22, 8);
        }

        if (snapshot.IsCharging == true)
        {
            using var bolt = new SolidBrush(Color.FromArgb(255, 255, 255));
            var points = new[]
            {
                new PointF(16, 4), new PointF(11, 15), new PointF(15, 15),
                new PointF(12, 27), new PointF(21, 13), new PointF(16, 13),
            };
            graphics.FillPolygon(bolt, points);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
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
