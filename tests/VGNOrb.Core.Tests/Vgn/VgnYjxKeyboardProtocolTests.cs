using VGNOrb.Devices.Vgn;

namespace VGNOrb.Core.Tests.Vgn;

/// <summary>
/// Frame format regression tests for the YJX keyboard family (VGN N87). The
/// frames are constructed from the official VGN HUB web driver's
/// YixKeyboardServe class (63-byte frames on report ID 0xF0: command, device
/// type 2, checksum over frame bytes 3..62, data length, address, profile,
/// data); they are not captured from physical hardware yet.
/// </summary>
public sealed class VgnYjxKeyboardProtocolTests
{
    [Fact]
    public void BuildBatteryReport_MatchesDriverFrameFormat()
    {
        var report = VgnYjxKeyboardProtocol.BuildBatteryReport(maxOutputReportLength: 64);

        Assert.Equal(64, report.Length);
        Assert.Equal("F03002" + "00" + "02" + new string('0', 118), Convert.ToHexString(report));
    }

    [Fact]
    public void TryParseBattery_ParsesWirelessLevel()
    {
        // Frame data: length 2, level 73, status 0 -> checksum = (2 + 73) & 0xFF.
        var report = BuildResponse(level: 73, status: 0x00);

        var parsed = VgnYjxKeyboardProtocol.TryParseBattery(report, out var battery);

        Assert.True(parsed);
        Assert.Equal(73, battery.Percentage);
        Assert.False(battery.IsCharging);
    }

    [Fact]
    public void TryParseBattery_WiredStatusMeansCharging()
    {
        var report = BuildResponse(level: 88, status: 0x01);

        var parsed = VgnYjxKeyboardProtocol.TryParseBattery(report, out var battery);

        Assert.True(parsed);
        Assert.Equal(88, battery.Percentage);
        Assert.True(battery.IsCharging);
    }

    [Fact]
    public void TryParseBattery_RejectsCorruptedChecksum()
    {
        var report = BuildResponse(level: 73, status: 0x00);
        report[3] ^= 0xFF;

        Assert.False(VgnYjxKeyboardProtocol.TryParseBattery(report, out _));
    }

    [Fact]
    public void TryParseBattery_RejectsUnrelatedReports()
    {
        var wrongReportId = BuildResponse(level: 73, status: 0x00);
        wrongReportId[0] = 0xF1;

        var wrongCommand = BuildResponse(level: 73, status: 0x00);
        wrongCommand[1] = 0x03;

        var wrongDeviceType = BuildResponse(level: 73, status: 0x00);
        wrongDeviceType[2] = 0x01;

        Assert.False(VgnYjxKeyboardProtocol.TryParseBattery(wrongReportId, out _));
        Assert.False(VgnYjxKeyboardProtocol.TryParseBattery(wrongCommand, out _));
        Assert.False(VgnYjxKeyboardProtocol.TryParseBattery(wrongDeviceType, out _));
    }

    [Fact]
    public void TryParseBattery_RejectsOutOfRangeLevel()
    {
        var report = BuildResponse(level: 0xFF, status: 0x00);

        Assert.False(VgnYjxKeyboardProtocol.TryParseBattery(report, out _));
    }

    private static byte[] BuildResponse(byte level, byte status)
    {
        var report = new byte[64];
        report[0] = 0xF0;
        report[1] = 0x30;
        report[2] = 0x02;
        report[4] = 0x02; // data length
        report[8] = level;
        report[9] = status;

        var sum = 0;
        for (var i = 4; i < 64; i++)
        {
            sum += report[i];
        }

        report[3] = (byte)(sum & 0xFF);
        return report;
    }
}
