using HidSharp;
using VGNOrb.Core.Devices;

namespace VGNOrb.Devices.Vgn;

/// <summary>
/// Finds the vendor HID collection (usage page 0xFF01, usage 0x10, report ID
/// 0xF0) of the YJX keyboard family: VGN N87 wired (0xA8A4:0x2355) and its
/// 2.4G dongle (0xA8A5:0x2255). The VID/PID pairs come from the official VGN
/// HUB web driver's device table; they have not been validated against
/// physical hardware yet.
/// </summary>
public sealed class VgnYjxKeyboardDiscoveryService : IDeviceDiscoveryService
{
    private const string DeviceIdPrefix = VgnYjxKeyboardDevice.DeviceIdPrefix + ":";
    private const uint VendorUsagePage = 0xFF01;
    private const uint VendorUsage = 0x0010;

    private static readonly IReadOnlyDictionary<int, HashSet<int>> SupportedDevices =
        new Dictionary<int, HashSet<int>>
        {
            [0xA8A4] = [0x2355],
            [0xA8A5] = [0x2255]
        };

    public Task<IReadOnlyList<DeviceDescriptor>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<DeviceDescriptor> result = FindDevices()
            .Select(device => new DeviceDescriptor(
                DeviceIdPrefix + device.DevicePath,
                ReadProductName(device) ?? "VGN N87",
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
            throw new ArgumentException("设备描述不属于 YJX 键盘发现服务。", nameof(descriptor));
        }

        var devicePath = descriptor.Id[DeviceIdPrefix.Length..];
        var hidDevice = FindDevices()
            .FirstOrDefault(device => string.Equals(
                device.DevicePath,
                devicePath,
                StringComparison.Ordinal))
            ?? throw new InvalidOperationException("VGN 键盘设备已断开，请重新发现。");

        var device = new VgnYjxKeyboardDevice(hidDevice);
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

        return devices.Where(IsSupportedCollection);
    }

    private static bool IsSupportedCollection(HidDevice device)
    {
        if (!SupportedDevices.TryGetValue(device.VendorID, out var productIds)
            || !productIds.Contains(device.ProductID))
        {
            return false;
        }

        try
        {
            if (device.GetMaxInputReportLength() < 1 + VgnYjxKeyboardProtocol.FrameLength
                || device.GetMaxOutputReportLength() < 1 + VgnYjxKeyboardProtocol.FrameLength)
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
