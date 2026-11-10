using VGNOrb.Core.Devices;
using VGNOrb.Core.Devices.Mock;

namespace VGNOrb.Core.Tests.Devices;

public sealed class MockDeviceDiscoveryServiceTests
{
    [Fact]
    public async Task Discover_ThenConnect_ReturnsConnectedMockDevice()
    {
        var discovery = new MockDeviceDiscoveryService(FastOptions());

        var devices = await discovery.DiscoverAsync();
        var descriptor = Assert.Single(devices);
        Assert.Equal("Mock", descriptor.Transport);
        Assert.Equal(DeviceKind.Simulated, descriptor.Kind);

        await using var device = await discovery.ConnectAsync(descriptor);

        Assert.IsType<MockDevice>(device);
        Assert.Equal(DeviceConnectionState.Connected, device.ConnectionState);
        Assert.Equal("Mock", device.Info.Transport);
    }

    [Fact]
    public async Task Connect_WithUnknownDescriptor_Throws()
    {
        var discovery = new MockDeviceDiscoveryService(FastOptions());
        var unknown = new DeviceDescriptor("unknown", "Unknown", "Mock");

        await Assert.ThrowsAsync<ArgumentException>(
            () => discovery.ConnectAsync(unknown));
    }

    private static MockDeviceOptions FastOptions()
    {
        return new MockDeviceOptions
        {
            ConnectDelay = TimeSpan.Zero,
            DisconnectDelay = TimeSpan.Zero,
            ReadDelay = TimeSpan.Zero
        };
    }
}
