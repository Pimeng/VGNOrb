using VGNOrb.Core.Devices;

namespace VGNOrb.Devices.Vgn;

/// <summary>
/// Wire format of the "yongjiaxin" mouse family (VGN Dragonfly3 SE/Pro,
/// Dragonfly Y2 V2 Pro/Plus, Dragonfly F1 V2 Pro/SE/Plus and their 2.4G
/// dongles), mapped from the official VGN HUB web driver's YjxServe class.
/// The vendor collection (usage page 0xFF01, usage 0x10) uses no report ID;
/// commands are 64-byte payloads starting with the 0x55 marker followed by the
/// command byte. The battery command is 0x30; in the response the data area at
/// payload offset 8 carries the level, and a wired-status byte at offset 9
/// (1 means the mouse is wired, which the driver treats as charging).
/// Mapped from the driver, not yet validated against physical hardware.
/// </summary>
public static class VgnYjxMouseProtocol
{
    public const byte ReportId = 0x00;
    public const byte CommandMarker = 0x55;
    public const byte BatteryLevelCommand = 0x30;
    public const int PayloadLength = 64;

    /// <summary>Payload offset of the command echo (payload excludes the report ID byte).</summary>
    public const int ResponseCommandOffset = 1;

    /// <summary>Payload offset of the battery data area.</summary>
    public const int ResponseDataOffset = 8;

    /// <summary>Builds the battery query as a full output report (report ID byte + payload).</summary>
    public static byte[] BuildBatteryReport(int maxOutputReportLength)
    {
        var report = new byte[Math.Max(maxOutputReportLength, 1 + PayloadLength)];
        report[0] = ReportId;
        report[1] = CommandMarker;
        report[2] = BatteryLevelCommand;
        return report;
    }

    public static bool TryParseBattery(ReadOnlySpan<byte> report, out BatteryStatus battery)
    {
        battery = null!; // assigned before returning true; BatteryStatus is a class
        if (report.Length < 1 + ResponseDataOffset + 2
            || report[0] != ReportId
            || report[1] != CommandMarker
            || report[1 + ResponseCommandOffset] != BatteryLevelCommand)
        {
            return false;
        }

        var percentage = report[1 + ResponseDataOffset];
        var isWired = report[1 + ResponseDataOffset + 1] == 1;
        if (percentage > 100)
        {
            return false;
        }

        // The official driver leaves the level untouched while wired; the level
        // byte is still reported here so the caller always gets a reading.
        battery = new BatteryStatus(percentage, isWired, DateTimeOffset.UtcNow);
        return true;
    }
}
