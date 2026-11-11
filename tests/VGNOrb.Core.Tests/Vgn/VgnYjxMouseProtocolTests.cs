using VGNOrb.Devices.Vgn;

namespace VGNOrb.Core.Tests.Vgn;

/// <summary>
/// Frame format regression tests for the yongjiaxin mouse family. The frames
/// are constructed from the official VGN HUB web driver's YjxServe class
/// (64-byte payloads without a report ID, 0x55 marker, battery command 0x30,
/// answer data area at payload offset 8); they are not captured from physical
/// hardware yet. Raw HidSharp reports carry the implicit report ID byte 0x00
/// in front of the payload.
/// </summary>
public sealed class VgnYjxMouseProtocolTests
{
    [Fact]
    public void BuildBatteryReport_MatchesDriverFrameFormat()
    {
        var report = VgnYjxMouseProtocol.BuildBatteryReport(maxOutputReportLength: 65);

        Assert.Equal(65, report.Length);
        Assert.Equal("005530" + new string('0', 124), Convert.ToHexString(report));
    }

    [Fact]
    public void TryParseBattery_ParsesWirelessLevel()
    {
        var report = new byte[65];
        report[1] = 0x55;
        report[2] = 0x30;
        report[9] = 60;

        var parsed = VgnYjxMouseProtocol.TryParseBattery(report, out var battery);

        Assert.True(parsed);
        Assert.Equal(60, battery.Percentage);
        Assert.False(battery.IsCharging);
    }

    [Fact]
    public void TryParseBattery_WiredStatusMeansCharging()
    {
        var report = new byte[65];
        report[1] = 0x55;
        report[2] = 0x30;
        report[9] = 100;
        report[10] = 0x01;

        var parsed = VgnYjxMouseProtocol.TryParseBattery(report, out var battery);

        Assert.True(parsed);
        Assert.Equal(100, battery.Percentage);
        Assert.True(battery.IsCharging);
    }

    [Fact]
    public void TryParseBattery_RejectsUnrelatedReports()
    {
        var wrongMarker = new byte[65];
        wrongMarker[1] = 0xAA;
        wrongMarker[2] = 0x30;

        var wrongCommand = new byte[65];
        wrongCommand[1] = 0x55;
        wrongCommand[2] = 0x31;

        Assert.False(VgnYjxMouseProtocol.TryParseBattery(wrongMarker, out _));
        Assert.False(VgnYjxMouseProtocol.TryParseBattery(wrongCommand, out _));
        Assert.False(VgnYjxMouseProtocol.TryParseBattery(new byte[8], out _));
    }

    [Fact]
    public void TryParseBattery_RejectsOutOfRangeLevel()
    {
        var report = new byte[65];
        report[1] = 0x55;
        report[2] = 0x30;
        report[9] = 0xFF;

        Assert.False(VgnYjxMouseProtocol.TryParseBattery(report, out _));
    }
}
