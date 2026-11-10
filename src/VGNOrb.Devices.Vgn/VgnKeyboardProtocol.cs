namespace VGNOrb.Devices.Vgn;

public readonly record struct VgnKeyboardBatteryReading(
    int Percentage,
    bool IsCharging,
    bool IsFull);

/// <summary>
/// Raw HID report format used by the VGN V98Pro V4 keyboard family. It is a
/// 64-byte report (report ID 9 + a 63-byte BYTECH packet) on usage page 0xFF60,
/// usage 0x61. The packet checksum is seeded with the report ID.
/// </summary>
public static class VgnKeyboardProtocol
{
    public const byte ReportId = 0x09;
    public const int ReportLength = 64;
    public const int ProtocolPacketLength = 63;
    public const int HeaderLength = 6;
    public const int MaximumDataLength = 56;
    public const int ChecksumIndex = 62;

    public const byte BatteryStatusCommand = 0x87;
    public const byte FirmwareVersionCommand = 0x82;
    public const byte FirmwareVersionParameter = 0x02;

    public static byte[] BuildCommandReport(
        byte command,
        byte parameter = 0,
        ReadOnlySpan<byte> data = default)
    {
        if (data.Length > MaximumDataLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(data),
                data.Length,
                $"The VGN keyboard protocol accepts at most {MaximumDataLength} data bytes.");
        }

        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[1] = command;
        report[2] = parameter;
        report[3] = 0;
        report[4] = 1;
        report[5] = 0;
        report[6] = (byte)data.Length;
        if (!data.IsEmpty)
        {
            data.CopyTo(report.AsSpan(7));
        }

        report[63] = ComputeChecksum(report.AsSpan(1, 62));
        return report;
    }

    public static bool HasValidFrame(ReadOnlySpan<byte> report)
    {
        if (report.Length != ReportLength
            || report[0] != ReportId
            || report[4] != 1
            || report[5] != 0)
        {
            return false;
        }

        return report[63] == ComputeChecksum(report[1..63]);
    }

    public static bool TryParseBattery(ReadOnlySpan<byte> report, out VgnKeyboardBatteryReading reading)
    {
        reading = default;
        if (!HasValidFrame(report)
            || report[1] != BatteryStatusCommand
            || report[6] < 2)
        {
            return false;
        }

        var percentage = report[7];
        var flags = report[8];
        if (percentage > 100)
        {
            return false;
        }

        var charging = ((flags & 0xF0) >> 4) != 0;
        var full = (flags & 0x0F) != 0;
        reading = new VgnKeyboardBatteryReading(percentage, charging, full);
        return true;
    }

    public static bool TryParseFirmwareVersion(ReadOnlySpan<byte> report, out string version)
    {
        version = string.Empty;
        if (!HasValidFrame(report)
            || report[1] != FirmwareVersionCommand
            || report[2] != FirmwareVersionParameter
            || report[6] < 2)
        {
            return false;
        }

        var rawValue = (report[8] << 8) | report[7];
        version =
            $"{(rawValue >> 8) & 0xFF}.{(rawValue >> 4) & 0x0F}.{rawValue & 0x0F}";
        return true;
    }

    private static byte ComputeChecksum(ReadOnlySpan<byte> packetBeforeChecksum)
    {
        var sum = (int)ReportId;
        foreach (var value in packetBeforeChecksum)
        {
            sum += value;
        }

        return (byte)((255 - (sum & 0xFF)) & 0xFF);
    }
}
