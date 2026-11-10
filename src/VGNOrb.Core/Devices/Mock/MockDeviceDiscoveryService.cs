namespace VGNOrb.Core.Devices.Mock;

public sealed class MockDeviceDiscoveryService : IDeviceDiscoveryService
{
    public const string DeviceId = "mock-orb-001";

    private static readonly DeviceDescriptor Descriptor =
        new(DeviceId, "VGN Orb · 模拟设备", "Mock", DeviceKind.Simulated);

    private readonly MockDeviceOptions _options;

    public MockDeviceDiscoveryService(MockDeviceOptions? options = null)
    {
        _options = options ?? new MockDeviceOptions();
    }

    public Task<IReadOnlyList<DeviceDescriptor>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<DeviceDescriptor> devices = [Descriptor];
        return Task.FromResult(devices);
    }

    public async Task<IDevice> ConnectAsync(
        DeviceDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(descriptor.Id, DeviceId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Unknown mock device endpoint '{descriptor.Id}'.",
                nameof(descriptor));
        }

        var device = new MockDevice(
            new DeviceInfo(
                DeviceId,
                "VGN Orb · 模拟设备",
                "Mock Lab",
                "Orb Simulator",
                "mock-1.0.0",
                "Mock"),
            _options);

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
}
