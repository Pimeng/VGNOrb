using HidSharp;
using VGNOrb.Core.Devices;

namespace VGNOrb.Devices.Vgn;

/// <summary>
/// Finds the vendor-defined HID collection used by VGN F1 SE mice. The public
/// VGN HUB web driver maps the wired and 2.4G receiver product IDs to the same
/// handler. The 0xFB51 wired PID and 0xFB50 receiver PID are both physically
/// validated with this adapter.
/// </summary>
public sealed class VgnHidDeviceDiscoveryService : IDeviceDiscoveryService
{
    public const int VendorId = 0x3554;
    public const int VerifiedProductId = 0xFB51;
    public const int WirelessReceiverProductId = 0xFB50;
    public const int AlternateF1SeProductId = 0xF511;
    private const string DeviceIdPrefix = "vgn-hid:";
    private const uint VendorUsagePage = 0xFF02;
    private const uint VendorUsage = 0x0002;

    public Task<IReadOnlyList<DeviceDescriptor>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<DeviceDescriptor> result = FindDevices()
            .Select(device => new DeviceDescriptor(
                DeviceIdPrefix + device.DevicePath,
                ReadProductName(device) ?? "VGN F1 SE",
                "USB HID",
                DeviceKind.Mouse))
            .ToArray();

        return Task.FromResult(result);
    }

    public async Task<IDevice> ConnectAsync(
        DeviceDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();

        if (!descriptor.Id.StartsWith(DeviceIdPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("设备描述不属于 VGN HID 发现服务。", nameof(descriptor));
        }

        var devicePath = descriptor.Id[DeviceIdPrefix.Length..];
        var hidDevice = FindDevices()
            .FirstOrDefault(device => string.Equals(
                device.DevicePath,
                devicePath,
                StringComparison.Ordinal))
            ?? throw new InvalidOperationException("VGN HID 设备已断开，请重新发现。");

        var device = new VgnHidDevice(hidDevice);
        try
        {
            await device.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return device;
        }
        catch
        {
            await device.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static IEnumerable<HidDevice> FindDevices()
    {
        HidDevice[] devices;
        try
        {
            devices = DeviceList.Local.GetHidDevices().ToArray();
        }
        catch (Exception)
        {
            return [];
        }

        return devices.Where(IsSupportedVgnCollection);
    }

    private static bool IsSupportedVgnCollection(HidDevice device)
    {
        if (device.VendorID != VendorId
            || (device.ProductID != VerifiedProductId
                && device.ProductID != WirelessReceiverProductId
                && device.ProductID != AlternateF1SeProductId))
        {
            return false;
        }

        try
        {
            if (device.GetMaxInputReportLength() < VgnJmProtocol.ReportLength
                || device.GetMaxOutputReportLength() < VgnJmProtocol.ReportLength)
            {
                return false;
            }

            return device.GetReportDescriptor().DeviceItems.Any(item =>
                item.Usages.GetAllValues().Any(value =>
                {
                    var usage = Convert.ToUInt32(value);
                    return usage == ((VendorUsagePage << 16) | VendorUsage);
                }));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? ReadProductName(HidDevice device)
    {
        try
        {
            return device.GetProductName();
        }
        catch
        {
            return null;
        }
    }
}
