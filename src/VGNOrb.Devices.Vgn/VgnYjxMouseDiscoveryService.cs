using HidSharp;
using VGNOrb.Core.Devices;

namespace VGNOrb.Devices.Vgn;

/// <summary>
/// Finds the vendor HID collection (usage page 0xFF01, usage 0x10) of the
/// yongjiaxin mouse family. The VID/PID pairs come from the official VGN HUB
/// web driver's device table and cover the Dragonfly3 SE/Pro, Dragonfly Y2 V2
/// Pro/Plus and Dragonfly F1 V2 Pro/SE/Plus models plus their 2.4G dongles;
/// none of them have been validated against physical hardware yet.
/// </summary>
public sealed class VgnYjxMouseDiscoveryService : IDeviceDiscoveryService
{
    private const string DeviceIdPrefix = VgnYjxMouseDevice.DeviceIdPrefix + ":";
    private const uint VendorUsagePage = 0xFF01;
    private const uint VendorUsage = 0x0010;

    private static readonly IReadOnlyDictionary<int, HashSet<int>> SupportedDevices =
        new Dictionary<int, HashSet<int>>
        {
            [0x391D] =
            [
                0x1001, 0x1A01, 0x1003, 0x1A03, 0x100A, 0x1A09, 0x100B,
                0x1A0A, 0x1017, 0x1A14, 0x1019, 0x1A16, 0x1018, 0x1A15
            ],
            [0x3837] = [0x4042]
        };

    public Task<IReadOnlyList<DeviceDescriptor>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<DeviceDescriptor> result = FindDevices()
            .Select(device => new DeviceDescriptor(
                DeviceIdPrefix + device.DevicePath,
                ReadProductName(device) ?? "VGN Mouse (YJX)",
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
            throw new ArgumentException("设备描述不属于 YJX 鼠标发现服务。", nameof(descriptor));
        }

        var devicePath = descriptor.Id[DeviceIdPrefix.Length..];
        var hidDevice = FindDevices()
            .FirstOrDefault(device => string.Equals(
                device.DevicePath,
                devicePath,
                StringComparison.Ordinal))
            ?? throw new InvalidOperationException("VGN 鼠标设备已断开，请重新发现。");

        var device = new VgnYjxMouseDevice(hidDevice);
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
            if (device.GetMaxInputReportLength() < 1 + VgnYjxMouseProtocol.PayloadLength
                || device.GetMaxOutputReportLength() < 1 + VgnYjxMouseProtocol.PayloadLength)
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
