using VGNOrb.Core.Devices;

namespace VGNOrb.Devices.Vgn;

/// <summary>
/// Wire format of the "weisheng" keyboard family (VGN S99 / V98pro / N75 PRO /
/// VXE75 / VXE V87 / S87 and their 2.4G dongles), mapped from the official VGN
/// HUB web driver's WsServe class. Vendor collection on report ID 4; the battery
/// query is the 7-byte command [00 00 1A 06 00 00 00] and the answer carries
/// the command echo at offset 3 of the raw report (offset 2 excluding the
/// report ID), followed by the level and charging flag in the data area.
/// Mapped from the driver, not yet validated against physical hardware.
/// </summary>
public static class VgnWsKeyboardProtocol
{
    public const byte ReportId = 0x04;
    public const byte BatteryLevelCommand = 0x1A;
    public const int CommandPayloadLength = 7;

    /// <summary>Offset of the command echo in the raw input report (incl. report ID).</summary>
    public const int ResponseCommandOffset = 3;

    /// <summary>Offset of the data area in the raw input report (incl. report ID).</summary>
    public const int ResponseDataOffset = 8;

    /// <summary>Builds the battery query as a full output report (report ID + payload).</summary>
    public static byte[] BuildBatteryReport(int maxOutputReportLength)
    {
        var report = new byte[Math.Max(maxOutputReportLength, 1 + CommandPayloadLength)];
        report[0] = ReportId;
        report[3] = BatteryLevelCommand;
        report[4] = 6;
        return report;
    }

    public static bool TryParseBattery(ReadOnlySpan<byte> report, out BatteryStatus battery)
    {
        battery = null!; // assigned before returning true; BatteryStatus is a class
        if (report.Length < ResponseDataOffset + 2
            || report[0] != ReportId
            || report[ResponseCommandOffset] != BatteryLevelCommand)
        {
            return false;
        }

        var percentage = report[ResponseDataOffset];
        if (percentage > 100)
        {
            return false;
        }

        battery = new BatteryStatus(
            percentage,
            report[ResponseDataOffset + 1] != 0,
            DateTimeOffset.UtcNow);
        return true;
    }
}
