using HidSharp;
using VGNOrb.Core.Devices;

namespace VGNOrb.Devices.Vgn;

/// <summary>
/// Read-only adapter for the yongjiaxin mouse family (no report ID, 0x55 marker).
/// Mapped from the official VGN HUB web driver's YjxServe class; not yet
/// validated against physical hardware.
/// </summary>
public sealed class VgnYjxMouseDevice : VgnProtocolDeviceBase
{
    public const string DeviceIdPrefix = "vgn-yjx-mouse";

    public VgnYjxMouseDevice(HidDevice hidDevice)
        : base(hidDevice, DeviceIdPrefix)
    {
    }

    protected override string ProtocolName => "VGN YJX mouse report 0";

    protected override string DefaultProductName => "VGN Mouse (YJX)";

    protected override byte[] BuildBatteryReport(int maxOutputReportLength) =>
        VgnYjxMouseProtocol.BuildBatteryReport(maxOutputReportLength);

    protected override bool TryParseBatteryReport(
        ReadOnlySpan<byte> report,
        out BatteryStatus battery) =>
        VgnYjxMouseProtocol.TryParseBattery(report, out battery);
}
