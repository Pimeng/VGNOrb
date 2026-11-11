using VGNOrb.Core.Devices;

namespace VGNOrb.Devices.Vgn;

/// <summary>
/// Wire format of the YJX keyboard family (VGN N87 and its 2.4G dongle), mapped
/// from the official VGN HUB web driver's YixKeyboardServe class. The vendor
/// collection (usage page 0xFF01, usage 0x10) carries 63-byte frames on report
/// ID 0xF0: command, device type (2 = keyboard), checksum (sum of frame bytes
/// 3..62, low byte), data length, little-endian address, frame profile, then
/// 56 data bytes. The battery command 0x30 returns two data bytes: level and
/// wired status (1 means wired, which the driver treats as charging).
/// Mapped from the driver, not yet validated against physical hardware.
/// </summary>
public static class VgnYjxKeyboardProtocol
{
    public const byte ReportId = 0xF0;
    public const byte BatteryLevelCommand = 0x30;
    public const byte KeyboardDeviceType = 0x02;
    public const int FrameLength = 63;
    public const int FrameDataOffset = 7;
    public const int BatteryDataSize = 2;
    public const byte WiredStatus = 0x01;

    /// <summary>Builds the battery query as a full output report (report ID + frame).</summary>
    public static byte[] BuildBatteryReport(int maxOutputReportLength)
    {
        var report = new byte[Math.Max(maxOutputReportLength, 1 + FrameLength)];
        report[0] = ReportId;
        report[1] = BatteryLevelCommand;
        report[2] = KeyboardDeviceType;
        // report[3] (checksum) stays 0 on requests, matching the reference driver.
        report[4] = BatteryDataSize;
        return report;
    }

    public static bool TryParseBattery(ReadOnlySpan<byte> report, out BatteryStatus battery)
    {
        battery = null!; // assigned before returning true; BatteryStatus is a class
        if (report.Length < 1 + FrameDataOffset + BatteryDataSize
            || report[0] != ReportId
            || report[1] != BatteryLevelCommand
            || report[2] != KeyboardDeviceType)
        {
            return false;
        }

        var dataLength = report[4];
        if (dataLength > 0 && report.Length >= 1 + FrameLength)
        {
            if (report[3] != ComputeChecksum(report.Slice(4, FrameLength - 3)))
            {
                return false;
            }
        }

        if (dataLength < BatteryDataSize)
        {
            return false;
        }

        var percentage = report[1 + FrameDataOffset];
        if (percentage > 100)
        {
            return false;
        }

        battery = new BatteryStatus(
            percentage,
            report[1 + FrameDataOffset + 1] == WiredStatus,
            DateTimeOffset.UtcNow);
        return true;
    }

    private static byte ComputeChecksum(ReadOnlySpan<byte> frameTail)
    {
        var sum = 0;
        foreach (var value in frameTail)
        {
            sum += value;
        }

        return (byte)(sum & 0xFF);
    }
}
