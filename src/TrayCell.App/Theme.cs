using System.Drawing;

namespace TrayCell.App;

/// <summary>
/// Colour tokens from the dekub.eu design system: near-black surfaces, 1px borders,
/// square corners, monochrome text, violet accent. Keep these in sync with
/// docs/design.md in the website repo.
/// </summary>
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(0x0A, 0x0A, 0x0C);
    public static readonly Color Surface = Color.FromArgb(0x11, 0x11, 0x15);
    public static readonly Color Border = Color.FromArgb(0x1E, 0x1E, 0x24);
    public static readonly Color BorderHover = Color.FromArgb(0x3A, 0x3A, 0x4A);
    public static readonly Color Text = Color.FromArgb(0xE8, 0xE8, 0xF0);
    public static readonly Color Muted = Color.FromArgb(0x5A, 0x5A, 0x6E);
    public static readonly Color Accent = Color.FromArgb(0x7B, 0x6C, 0xFF);
    public static readonly Color AccentSoft = Color.FromArgb(31, 0x7B, 0x6C, 0xFF); // accent at 12%
    public static readonly Color Red = Color.FromArgb(0xFF, 0x4F, 0x4F);
    public static readonly Color Green = Color.FromArgb(0x4F, 0xFF, 0x8A);
}
