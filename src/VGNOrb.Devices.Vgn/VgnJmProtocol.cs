namespace VGNOrb.Devices.Vgn;

public readonly record struct VgnBatteryReading(
    int Percentage,
    bool IsCharging,
    int VoltageMillivolts);

/// <summary>
/// Wire format used by the public VGN HUB web driver for the F1 SE family.
/// It is a vendor-defined HID collection (usage page 0xFF02, usage 0x0002)
/// using report ID 8. Each report is 17 bytes: report ID, 15 protocol bytes,
/// and a final checksum byte. All 17 bytes sum to 0x55 modulo 256.
/// </summary>
public static class VgnJmProtocol
{
    public const byte ReportId = 0x08;
    public const int ReportLength = 17;
    public const int MaximumPayloadLength = 10;
    public const byte ChecksumTarget = 0x55;

    public const byte DeviceOnlineCommand = 0x03;
    public const byte BatteryLevelCommand = 0x04;
    public const byte ReadVersionCommand = 0x12;

    public static byte[] BuildCommandPacket(byte command, ReadOnlySpan<byte> data = default)
    {
        if (data.Length > MaximumPayloadLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(data),
                data.Length,
                $"The VGN JM protocol accepts at most {MaximumPayloadLength} data bytes.");
        }

        var packet = new byte[ReportLength];
        packet[0] = ReportId;
        packet[1] = command;

        if (!data.IsEmpty)
        {
            packet[5] = (byte)data.Length;
            data.CopyTo(packet.AsSpan(6));
        }

        packet[16] = ComputeChecksum(packet.AsSpan(0, 16));
        return packet;
    }

    public static bool HasValidFrame(ReadOnlySpan<byte> report)
    {
        if (report.Length != ReportLength || report[0] != ReportId)
        {
            return false;
        }

        var sum = 0;
        foreach (var value in report)
        {
            sum += value;
        }

        return (sum & 0xFF) == ChecksumTarget;
    }

    public static bool TryParseDeviceOnline(ReadOnlySpan<byte> report, out bool online)
    {
        online = false;
        if (!IsSuccessfulResponse(report, DeviceOnlineCommand))
        {
            return false;
        }

        online = report[6] != 0;
        return true;
    }

    public static bool TryParseBattery(ReadOnlySpan<byte> report, out VgnBatteryReading reading)
    {
        reading = default;
        if (!IsSuccessfulResponse(report, BatteryLevelCommand))
        {
            return false;
        }

        var percentage = report[6];
        var charging = report[7] != 0;
        var voltage = (report[8] << 8) | report[9];
        if (percentage > 100 || voltage <= 0)
        {
            return false;
        }

        reading = new VgnBatteryReading(percentage, charging, voltage);
        return true;
    }

    public static bool TryParseFirmwareVersion(ReadOnlySpan<byte> report, out string version)
    {
        version = string.Empty;
        if (!IsSuccessfulResponse(report, ReadVersionCommand))
        {
            return false;
        }

        version = $"{report[6]}{report[7]:X2}";
        return true;
    }

    private static bool IsSuccessfulResponse(ReadOnlySpan<byte> report, byte expectedCommand)
    {
        return HasValidFrame(report)
            && report[1] == expectedCommand
            && report[2] == 0;
    }

    private static byte ComputeChecksum(ReadOnlySpan<byte> bytesBeforeChecksum)
    {
        var sum = 0;
        foreach (var value in bytesBeforeChecksum)
        {
            sum += value;
        }

        return (byte)((ChecksumTarget - (sum & 0xFF)) & 0xFF);
    }
}
