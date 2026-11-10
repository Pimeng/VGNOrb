using HidSharp;
using VGNOrb.Core.Devices;

namespace VGNOrb.Devices.Vgn;

/// <summary>
/// Finds the V98Pro V4 raw HID collection used by the public VGN HUB web driver.
/// The wired 0x200B and receiver 0x2A03 PIDs are physically validated; the other
/// PIDs are the same V98Pro V4 family in the official web-driver device table.
/// </summary>
public sealed class VgnKeyboardDiscoveryService : IDeviceDiscoveryService
{
    public const int VendorId = 0x391D;
    public const int VerifiedWiredProductId = 0x200B;
    private const string DeviceIdPrefix = "vgn-keyboard:";
    private const uint VendorUsagePage = 0xFF60;
    private const uint VendorUsage = 0x0061;

    private static readonly HashSet<int> SupportedProductIds =
    [
        VerifiedWiredProductId,
        0x2A03,
        0x201C,
        0x2A0A
    ];

    public Task<IReadOnlyList<DeviceDescriptor>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<DeviceDescriptor> result = FindDevices()
            .Select(device => new DeviceDescriptor(
                DeviceIdPrefix + device.DevicePath,
                ReadProductName(device) ?? "VGN V98Pro V4",
                "USB HID",
                DeviceKind.Keyboard))
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
            throw new ArgumentException("设备描述不属于 VGN 键盘发现服务。", nameof(descriptor));
        }

        var devicePath = descriptor.Id[DeviceIdPrefix.Length..];
        var hidDevice = FindDevices()
            .FirstOrDefault(device => string.Equals(
                device.DevicePath,
                devicePath,
                StringComparison.Ordinal))
            ?? throw new InvalidOperationException("VGN 键盘已断开，请重新发现。");

        var device = new VgnKeyboardDevice(hidDevice);
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

        return devices.Where(IsSupportedKeyboardCollection);
    }

    private static bool IsSupportedKeyboardCollection(HidDevice device)
    {
        if (device.VendorID != VendorId
            || !SupportedProductIds.Contains(device.ProductID))
        {
            return false;
        }

        try
        {
            if (device.GetMaxInputReportLength() < VgnKeyboardProtocol.ReportLength
                || device.GetMaxOutputReportLength() < VgnKeyboardProtocol.ReportLength)
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
