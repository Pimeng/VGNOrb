using HidSharp;
using VGNOrb.Core.Devices;

namespace VGNOrb.Devices.Vgn;

/// <summary>
/// Finds the vendor HID collection of the weisheng keyboard family
/// (VID 0x320F with PIDs 0x5055 wired / 0x5088 dongle, VID 0x373B with PIDs
/// 0x1028 wired / 0x1029 dongle). The VID/PID pairs come from the official VGN
/// HUB web driver's device table and cover the S99 / V98pro / V98pro V2 /
/// N75 PRO / VXE75 / VXE75 V2 / VXE V87 / S87 models; none of them have been
/// validated against physical hardware yet.
/// </summary>
public sealed class VgnWsKeyboardDiscoveryService : IDeviceDiscoveryService
{
    private const string DeviceIdPrefix = VgnWsKeyboardDevice.DeviceIdPrefix + ":";

    private static readonly IReadOnlyDictionary<int, HashSet<int>> SupportedDevices =
        new Dictionary<int, HashSet<int>>
        {
            [0x320F] = [0x5055, 0x5088],
            [0x373B] = [0x1028, 0x1029]
        };

    public Task<IReadOnlyList<DeviceDescriptor>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<DeviceDescriptor> result = FindDevices()
            .Select(device => new DeviceDescriptor(
                DeviceIdPrefix + device.DevicePath,
                ReadProductName(device) ?? "VGN Keyboard (WS)",
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
            throw new ArgumentException("设备描述不属于 weisheng 键盘发现服务。", nameof(descriptor));
        }

        var devicePath = descriptor.Id[DeviceIdPrefix.Length..];
        var hidDevice = FindDevices()
            .FirstOrDefault(device => string.Equals(
                device.DevicePath,
                devicePath,
                StringComparison.Ordinal))
            ?? throw new InvalidOperationException("VGN 键盘设备已断开，请重新发现。");

        var device = new VgnWsKeyboardDevice(hidDevice);
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
            // The vendor collection needs room for the report ID, the command
            // echo and the two battery data bytes.
            if (device.GetMaxInputReportLength() < VgnWsKeyboardProtocol.ResponseDataOffset + 2
                || device.GetMaxOutputReportLength() < 1 + VgnWsKeyboardProtocol.CommandPayloadLength)
            {
                return false;
            }

            // The weisheng table entry pins no usage page; accept any
            // vendor-defined page to skip the boot keyboard / consumer
            // collections on the same VID/PID.
            return device.GetReportDescriptor().DeviceItems.Any(item =>
                item.Usages.GetAllValues().Any(value =>
                    (Convert.ToUInt32(value) >> 16) >= 0xFF00));
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
