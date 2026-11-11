using VGNOrb.Devices.Vgn;

namespace VGNOrb.Core.Tests.Vgn;

/// <summary>
/// Frame format regression tests for the weisheng keyboard family. The frames
/// are constructed from the official VGN HUB web driver's WsServe class
/// (command [00 00 1A 06 00 00 00] on report ID 4, answer echoes the command
/// at payload offset 2 and carries level/charging at payload offsets 7/8);
/// they are not captured from physical hardware yet.
/// </summary>
public sealed class VgnWsKeyboardProtocolTests
{
    [Fact]
    public void BuildBatteryReport_MatchesDriverFrameFormat()
    {
        var report = VgnWsKeyboardProtocol.BuildBatteryReport(maxOutputReportLength: 9);

        Assert.Equal("0400001A0600000000", Convert.ToHexString(report));
    }

    [Fact]
    public void BuildBatteryReport_PadsToDeviceReportLength()
    {
        var report = VgnWsKeyboardProtocol.BuildBatteryReport(maxOutputReportLength: 65);

        Assert.Equal(65, report.Length);
        Assert.Equal(VgnWsKeyboardProtocol.ReportId, report[0]);
        Assert.Equal(VgnWsKeyboardProtocol.BatteryLevelCommand, report[3]);
    }

    [Fact]
    public void TryParseBattery_ParsesLevelAndCharging()
    {
        var report = new byte[]
        {
            0x04, 0x00, 0x00, 0x1A, 0x00, 0x00, 0x00, 0x00, 85, 0x01
        };

        var parsed = VgnWsKeyboardProtocol.TryParseBattery(report, out var battery);

        Assert.True(parsed);
        Assert.Equal(85, battery.Percentage);
        Assert.True(battery.IsCharging);
    }

    [Fact]
    public void TryParseBattery_ParsesDischarging()
    {
        var report = new byte[]
        {
            0x04, 0x00, 0x00, 0x1A, 0x00, 0x00, 0x00, 0x00, 42, 0x00
        };

        var parsed = VgnWsKeyboardProtocol.TryParseBattery(report, out var battery);

        Assert.True(parsed);
        Assert.Equal(42, battery.Percentage);
        Assert.False(battery.IsCharging);
    }

    [Fact]
    public void TryParseBattery_RejectsUnrelatedReports()
    {
        var wrongReportId = new byte[]
        {
            0x05, 0x00, 0x00, 0x1A, 0x00, 0x00, 0x00, 0x00, 85, 0x01
        };
        var wrongCommand = new byte[]
        {
            0x04, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 85, 0x01
        };
        var tooShort = new byte[] { 0x04, 0x00, 0x00, 0x1A };

        Assert.False(VgnWsKeyboardProtocol.TryParseBattery(wrongReportId, out _));
        Assert.False(VgnWsKeyboardProtocol.TryParseBattery(wrongCommand, out _));
        Assert.False(VgnWsKeyboardProtocol.TryParseBattery(tooShort, out _));
    }

    [Fact]
    public void TryParseBattery_RejectsOutOfRangeLevel()
    {
        var report = new byte[]
        {
            0x04, 0x00, 0x00, 0x1A, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x00
        };

        Assert.False(VgnWsKeyboardProtocol.TryParseBattery(report, out _));
    }
}
