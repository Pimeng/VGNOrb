using VGNOrb.Core.Devices;
using VGNOrb.Devices.Vgn;

namespace VGNOrb.Core.Tests.Vgn;

public sealed class VgnJmProtocolTests
{
    [Fact]
    public void BuildCommandPacket_Online_MatchesCapturedHardwareFrame()
    {
        var packet = VgnJmProtocol.BuildCommandPacket(VgnJmProtocol.DeviceOnlineCommand);

        Assert.Equal(
            "080300000000000000000000000000004A",
            Convert.ToHexString(packet));
        Assert.True(VgnJmProtocol.HasValidFrame(packet));
    }

    [Fact]
    public void BuildCommandPacket_Battery_MatchesCapturedHardwareFrame()
    {
        var packet = VgnJmProtocol.BuildCommandPacket(VgnJmProtocol.BatteryLevelCommand);

        Assert.Equal(
            "0804000000000000000000000000000049",
            Convert.ToHexString(packet));
    }

    [Fact]
    public void TryParseBattery_ParsesCapturedHardwareResponse()
    {
        var report = Convert.FromHexString(
            "080400000002640110560000000000007C");

        var parsed = VgnJmProtocol.TryParseBattery(report, out var reading);

        Assert.True(parsed);
        Assert.Equal(100, reading.Percentage);
        Assert.True(reading.IsCharging);
        Assert.Equal(4182, reading.VoltageMillivolts);
    }

    [Fact]
    public void TryParseDeviceOnline_ParsesCapturedHardwareResponse()
    {
        var report = Convert.FromHexString(
            "080300000001019D85190000000000000D");

        var parsed = VgnJmProtocol.TryParseDeviceOnline(report, out var online);

        Assert.True(parsed);
        Assert.True(online);
    }

    [Fact]
    public void TryParseFirmwareVersion_ParsesCapturedHardwareResponse()
    {
        var report = Convert.FromHexString(
            "0812000000020313000000000000000023");

        var parsed = VgnJmProtocol.TryParseFirmwareVersion(report, out var version);

        Assert.True(parsed);
        Assert.Equal("313", version);
    }

    [Fact]
    public void HasValidFrame_RejectsCorruptedChecksum()
    {
        var report = Convert.FromHexString(
            "080400000002640110560000000000007C");
        report[^1] ^= 0xFF;

        Assert.False(VgnJmProtocol.HasValidFrame(report));
        Assert.False(VgnJmProtocol.TryParseBattery(report, out _));
    }

    [Fact]
    public void BuildCommandPacket_RejectsOversizedData()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VgnJmProtocol.BuildCommandPacket(
                VgnJmProtocol.BatteryLevelCommand,
                new byte[VgnJmProtocol.MaximumPayloadLength + 1]));
    }

    [Fact]
    public void BatteryStatus_CarriesOptionalVoltage()
    {
        var status = new BatteryStatus(64, true, DateTimeOffset.UtcNow, 3980);

        Assert.Equal(64, status.Percentage);
        Assert.True(status.IsCharging);
        Assert.Equal(3980, status.VoltageMillivolts);
    }
}
