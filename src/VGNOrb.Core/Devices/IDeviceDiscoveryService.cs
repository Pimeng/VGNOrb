namespace VGNOrb.Core.Devices;

public interface IDeviceDiscoveryService
{
    Task<IReadOnlyList<DeviceDescriptor>> DiscoverAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the selected endpoint and returns an already connected device.
    /// The caller owns the returned instance.
    /// </summary>
    Task<IDevice> ConnectAsync(
        DeviceDescriptor descriptor,
        CancellationToken cancellationToken = default);
}
