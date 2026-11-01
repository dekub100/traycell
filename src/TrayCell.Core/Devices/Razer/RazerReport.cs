namespace TrayCell.Devices.Razer;

/// <summary>
/// The 90-byte Razer control report used by OpenRazer (class 0x07 commands).
/// Layout: status, transaction_id, remaining_packets[2], protocol_type, data_size,
/// command_class, command_id, arguments[80], crc, reserved.
/// </summary>
public static class RazerReport
{
    public const int Length = 90;
    public const byte TransactionId = 0x1f;

    public const byte ClassBattery = 0x07;
    public const byte CommandBatteryLevel = 0x80;
    public const byte CommandChargingStatus = 0x84;

    public const byte StatusSuccess = 0x02;
    public const byte StatusFailure = 0x03;
    public const byte StatusTimeout = 0x04;

    private const byte DefaultDataSize = 0x02;
    private const int OffsetDataSize = 5;
    private const int OffsetCommandClass = 6;
    private const int OffsetCommandId = 7;
    private const int OffsetCrc = 88;

    public static byte[] CreateBatteryLevelRequest() => Create(ClassBattery, CommandBatteryLevel);

    public static byte[] CreateChargingStatusRequest() => Create(ClassBattery, CommandChargingStatus);

    public static byte[] Create(byte commandClass, byte commandId, byte dataSize = DefaultDataSize)
    {
        var report = new byte[Length];
        report[1] = TransactionId;
        report[OffsetDataSize] = dataSize;
        report[OffsetCommandClass] = commandClass;
        report[OffsetCommandId] = commandId;
        report[OffsetCrc] = ComputeCrc(report);
        return report;
    }

    /// <summary>Simple XOR checksum over bytes 2..87, stored at index 88.</summary>
    public static byte ComputeCrc(ReadOnlySpan<byte> report)
    {
        byte crc = 0;
        for (var i = 2; i < 88 && i < report.Length; i++)
        {
            crc ^= report[i];
        }

        return crc;
    }

    public static bool TryParse(ReadOnlySpan<byte> response, out RazerResponse parsed)
    {
        if (response.Length < Length)
        {
            parsed = default;
            return false;
        }

        parsed = new RazerResponse(
            Status: response[0],
            TransactionId: response[1],
            DataSize: response[OffsetDataSize],
            CommandClass: response[OffsetCommandClass],
            CommandId: response[OffsetCommandId],
            Arguments: response[8..88].ToArray(),
            CrcValid: ComputeCrc(response) == response[OffsetCrc]);

        return true;
    }
}

public readonly record struct RazerResponse(
    byte Status,
    byte TransactionId,
    byte DataSize,
    byte CommandClass,
    byte CommandId,
    byte[] Arguments,
    bool CrcValid)
{
    public bool IsSuccess => Status == RazerReport.StatusSuccess;

    /// <summary>Battery level (0-100) or charging flag, per OpenRazer: argument index 1.</summary>
    public int Argument1 => Arguments.Length > 1 ? Arguments[1] : -1;

    public string Describe()
    {
        var status = Status switch
        {
            RazerReport.StatusSuccess => "success",
            RazerReport.StatusFailure => "failure",
            RazerReport.StatusTimeout => "timeout/no-response",
            _ => $"0x{Status:X2}",
        };

        return $"status={status} tx=0x{TransactionId:X2} class=0x{CommandClass:X2} " +
               $"cmd=0x{CommandId:X2} args[0..3]={string.Join(",", Arguments.Take(4))} crc={(CrcValid ? "ok" : "bad")}";
    }
}
