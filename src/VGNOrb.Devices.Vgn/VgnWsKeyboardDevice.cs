using HidSharp;
using VGNOrb.Core.Devices;

namespace VGNOrb.Devices.Vgn;

/// <summary>
/// Read-only adapter for the weisheng keyboard family (report ID 4). Mapped from
/// the official VGN HUB web driver's WsServe class; not yet validated against
/// physical hardware.
/// </summary>
public sealed class VgnWsKeyboardDevice : VgnProtocolDeviceBase
{
    public const string DeviceIdPrefix = "vgn-ws-keyboard";

    public VgnWsKeyboardDevice(HidDevice hidDevice)
        : base(hidDevice, DeviceIdPrefix)
    {
    }

    protected override string ProtocolName => "VGN WS keyboard report 4";

    protected override string DefaultProductName => "VGN Keyboard (WS)";

    protected override byte[] BuildBatteryReport(int maxOutputReportLength) =>
        VgnWsKeyboardProtocol.BuildBatteryReport(maxOutputReportLength);

    protected override bool TryParseBatteryReport(
        ReadOnlySpan<byte> report,
        out BatteryStatus battery) =>
        VgnWsKeyboardProtocol.TryParseBattery(report, out battery);
}
