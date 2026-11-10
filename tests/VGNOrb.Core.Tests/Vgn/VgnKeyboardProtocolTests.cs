using VGNOrb.Devices.Vgn;

namespace VGNOrb.Core.Tests.Vgn;

public sealed class VgnKeyboardProtocolTests
{
    [Fact]
    public void BuildBatteryQuery_MatchesCapturedHardwareFrame()
    {
        var report = VgnKeyboardProtocol.BuildCommandReport(
            VgnKeyboardProtocol.BatteryStatusCommand);

        Assert.Equal(
            "0987000001000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000006E",
            Convert.ToHexString(report));
    }

    [Fact]
    public void BuildFirmwareQuery_MatchesCapturedHardwareFrame()
    {
        var report = VgnKeyboardProtocol.BuildCommandReport(
            VgnKeyboardProtocol.FirmwareVersionCommand,
            VgnKeyboardProtocol.FirmwareVersionParameter,
            [0, 0]);

        Assert.Equal(
            "0982020001000200000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000006F",
            Convert.ToHexString(report));
    }

    [Fact]
    public void TryParseBattery_ParsesCapturedHardwareResponse()
    {
        var report = Convert.FromHexString(
            "098700000100026411000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000F7");

        var parsed = VgnKeyboardProtocol.TryParseBattery(report, out var reading);

        Assert.True(parsed);
        Assert.Equal(100, reading.Percentage);
        Assert.True(reading.IsCharging);
        Assert.True(reading.IsFull);
    }

    [Fact]
    public void TryParseFirmwareVersion_ParsesCapturedHardwareResponse()
    {
        var report = Convert.FromHexString(
            "09820200010002201700000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000038");

        var parsed = VgnKeyboardProtocol.TryParseFirmwareVersion(report, out var version);

        Assert.True(parsed);
        Assert.Equal("23.2.0", version);
    }

    [Fact]
    public void HasValidFrame_RejectsCorruptedChecksum()
    {
        var report = Convert.FromHexString(
            "098700000100026411000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000F7");
        report[^1] ^= 0xFF;

        Assert.False(VgnKeyboardProtocol.HasValidFrame(report));
        Assert.False(VgnKeyboardProtocol.TryParseBattery(report, out _));
    }

    [Fact]
    public void BuildCommandReport_RejectsOversizedData()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VgnKeyboardProtocol.BuildCommandReport(
                VgnKeyboardProtocol.BatteryStatusCommand,
                data: new byte[VgnKeyboardProtocol.MaximumDataLength + 1]));
    }
}
