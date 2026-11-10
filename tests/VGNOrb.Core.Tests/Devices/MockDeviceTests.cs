using VGNOrb.Core.Devices;
using VGNOrb.Core.Devices.Mock;

namespace VGNOrb.Core.Tests.Devices;

public sealed class MockDeviceTests
{
    private static readonly DeviceInfo DeviceInfo = new(
        "test-mock",
        "Test Orb",
        "Test Lab",
        "Orb",
        "test-1.0",
        "Mock");

    [Fact]
    public async Task ConnectAndDisconnect_PublishStateTransitions()
    {
        await using var device = new MockDevice(DeviceInfo, FastOptions());
        var states = new List<DeviceConnectionState>();
        device.ConnectionStateChanged += (_, e) => states.Add(e.CurrentState);

        await device.ConnectAsync();
        Assert.Equal(DeviceConnectionState.Connected, device.ConnectionState);

        await device.DisconnectAsync();
        Assert.Equal(DeviceConnectionState.Disconnected, device.ConnectionState);

        Assert.Equal(
            [
                DeviceConnectionState.Connecting,
                DeviceConnectionState.Connected,
                DeviceConnectionState.Disconnecting,
                DeviceConnectionState.Disconnected
            ],
            states);
    }

    [Fact]
    public async Task ReadBattery_DrainsAndSimulatesLowBatteryCharging()
    {
        await using var device = new MockDevice(
            DeviceInfo,
            FastOptions() with
            {
                InitialBatteryPercentage = 50,
                BatteryDrainPerRead = 10,
                ChargeStepPerRead = 50,
                LowBatteryThreshold = 20
            });
        await device.ConnectAsync();

        var first = await device.ReadBatteryAsync();
        var second = await device.ReadBatteryAsync();
        var third = await device.ReadBatteryAsync();
        var fourth = await device.ReadBatteryAsync();
        var fifth = await device.ReadBatteryAsync();

        Assert.Equal(40, first.Percentage);
        Assert.False(first.IsCharging);
        Assert.Equal(30, second.Percentage);
        Assert.False(second.IsCharging);
        Assert.Equal(20, third.Percentage);
        Assert.True(third.IsCharging);
        Assert.Equal(70, fourth.Percentage);
        Assert.True(fourth.IsCharging);
        Assert.Equal(100, fifth.Percentage);
        Assert.False(fifth.IsCharging);
    }

    [Fact]
    public async Task ReadParameters_AndWriteParameter_RoundTrip()
    {
        await using var device = new MockDevice(DeviceInfo, FastOptions());
        await device.ConnectAsync();

        var initial = await device.ReadParametersAsync();
        Assert.Equal("balanced", initial["profile"]);

        await device.WriteParameterAsync("profile", "competitive");

        var updated = await device.ReadParametersAsync();
        Assert.Equal("competitive", updated["profile"]);
        Assert.Equal("1600", updated["dpi"]);
    }

    [Fact]
    public async Task ConnectAsync_WhenCanceled_ReturnsToDisconnectedState()
    {
        await using var device = new MockDevice(
            DeviceInfo,
            FastOptions() with { ConnectDelay = TimeSpan.FromSeconds(5) });
        using var cancellation = new CancellationTokenSource();

        var connectTask = device.ConnectAsync(cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connectTask);
        Assert.Equal(DeviceConnectionState.Disconnected, device.ConnectionState);
    }

    [Fact]
    public async Task ReadBatteryAsync_WhenDisconnected_Throws()
    {
        await using var device = new MockDevice(DeviceInfo, FastOptions());

        await Assert.ThrowsAsync<InvalidOperationException>(() => device.ReadBatteryAsync());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void BatteryStatus_RejectsOutOfRangePercentage(int percentage)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BatteryStatus(percentage, false, DateTimeOffset.UtcNow));
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
