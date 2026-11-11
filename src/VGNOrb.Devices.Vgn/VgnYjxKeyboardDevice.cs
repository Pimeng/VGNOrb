using HidSharp;
using VGNOrb.Core.Devices;

namespace VGNOrb.Devices.Vgn;

/// <summary>
/// Read-only adapter for the YJX keyboard family (report ID 0xF0, 63-byte
/// checksummed frames). Mapped from the official VGN HUB web driver's
/// YixKeyboardServe class; not yet validated against physical hardware.
/// </summary>
public sealed class VgnYjxKeyboardDevice : VgnProtocolDeviceBase
{
    public const string DeviceIdPrefix = "vgn-yjx-keyboard";

    public VgnYjxKeyboardDevice(HidDevice hidDevice)
        : base(hidDevice, DeviceIdPrefix)
    {
    }

    protected override string ProtocolName => "VGN YJX keyboard report 240";

    protected override string DefaultProductName => "VGN N87";

    protected override byte[] BuildBatteryReport(int maxOutputReportLength) =>
        VgnYjxKeyboardProtocol.BuildBatteryReport(maxOutputReportLength);

    protected override bool TryParseBatteryReport(
        ReadOnlySpan<byte> report,
        out BatteryStatus battery) =>
        VgnYjxKeyboardProtocol.TryParseBattery(report, out battery);
}
