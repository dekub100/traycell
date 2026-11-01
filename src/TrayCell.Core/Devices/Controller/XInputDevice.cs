using System.Runtime.InteropServices;

namespace TrayCell.Devices.Controller;

/// <summary>
/// Minimal XInput interop. XInput reports a battery *type* (wired / alkaline / nimh)
/// and a coarse *level* (empty / low / medium / full). USB cables and the 2.4 GHz
/// dongle always report "wired / full", so XInput can confirm a connection but can
/// never supply a percentage.
/// </summary>
public static class XInputDevice
{
    public const byte TypeDisconnected = 0;
    public const byte TypeWired = 1;
    public const byte TypeAlkaline = 2;
    public const byte TypeNiMH = 3;

    public const byte LevelEmpty = 0;
    public const byte LevelLow = 1;
    public const byte LevelMedium = 2;
    public const byte LevelFull = 3;

    private const byte DeviceTypeGamepad = 0;

    /// <summary>One connected controller slot and what XInput claims about its battery.</summary>
    public readonly record struct BatteryInfo(int Slot, byte Type, byte Level);

    [StructLayout(LayoutKind.Sequential)]
    private struct Gamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short LeftThumbX;
        public short LeftThumbY;
        public short RightThumbX;
        public short RightThumbY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        public uint PacketNumber;
        public Gamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryInformation
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint userIndex, out State state);

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetBatteryInformation(uint userIndex, byte deviceType, out BatteryInformation information);

    /// <summary>Every connected XInput slot, with its (coarse) battery information.</summary>
    public static List<BatteryInfo> ReadBatteries()
    {
        var results = new List<BatteryInfo>();

        for (uint slot = 0; slot < 4; slot++)
        {
            if (XInputGetState(slot, out _) != 0)
            {
                continue;
            }

            results.Add(XInputGetBatteryInformation(slot, DeviceTypeGamepad, out var battery) == 0
                ? new BatteryInfo((int)slot, battery.BatteryType, battery.BatteryLevel)
                : new BatteryInfo((int)slot, TypeDisconnected, LevelEmpty));
        }

        return results;
    }

    public static bool IsAnyConnected() => ReadBatteries().Count > 0;

    public static string DescribeType(byte type) => type switch
    {
        TypeDisconnected => "disconnected",
        TypeWired => "wired",
        TypeAlkaline => "alkaline",
        TypeNiMH => "nimh",
        _ => $"0x{type:X2}",
    };

    public static string DescribeLevel(byte level) => level switch
    {
        LevelEmpty => "empty",
        LevelLow => "low",
        LevelMedium => "medium",
        LevelFull => "full",
        _ => $"0x{level:X2}",
    };
}
