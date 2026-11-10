using VGNOrb.Core.Devices;
using VGNOrb.Core.Devices.Mock;
using VGNOrb.Core.Polling;

namespace VGNOrb.Core.Tests.Polling;

public sealed class DevicePollingServiceTests
{
    [Fact]
    public async Task Polling_ReportsBatteryUntilStopped()
    {
        await using var device = new MockDevice(
            DeviceInfo,
            new MockDeviceOptions
            {
                ConnectDelay = TimeSpan.Zero,
                DisconnectDelay = TimeSpan.Zero,
                ReadDelay = TimeSpan.Zero,
                InitialBatteryPercentage = 77,
                BatteryDrainPerRead = 0,
                LowBatteryThreshold = 0
            });
        await device.ConnectAsync();

        await using var polling = new DevicePollingService(TimeSpan.FromMilliseconds(15));
        var firstBattery = new TaskCompletionSource<BatteryStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        polling.BatteryUpdated += (_, e) => firstBattery.TrySetResult(e.Battery);

        await polling.StartAsync(device);
        Assert.True(polling.IsRunning);

        var battery = await firstBattery.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(77, battery.Percentage);

        await polling.StopAsync();
        Assert.False(polling.IsRunning);
    }

    [Fact]
    public async Task Polling_SurvivesTransientReadFailure()
    {
        var device = new FlakyDevice();
        await using var polling = new DevicePollingService(TimeSpan.FromMilliseconds(15));
        var failure = new TaskCompletionSource<PollingErrorEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var battery = new TaskCompletionSource<BatteryUpdatedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        polling.PollingFailed += (_, e) => failure.TrySetResult(e);
        polling.BatteryUpdated += (_, e) => battery.TrySetResult(e);

        await polling.StartAsync(device);

        var reportedFailure = await failure.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var reportedBattery = await battery.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<InvalidOperationException>(reportedFailure.Error);
        Assert.Equal(42, reportedBattery.Battery.Percentage);

        await polling.StopAsync();
    }

    [Fact]
    public void Constructor_RejectsNonPositiveInterval()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DevicePollingService(TimeSpan.Zero));
    }

    private static readonly DeviceInfo DeviceInfo = new(
        "polling-test",
        "Polling Orb",
        "Test Lab",
        "Orb",
        "test-1.0",
        "Mock");

    private sealed class FlakyDevice : IDevice
    {
        private int _readCount;

        public DeviceInfo Info { get; } = new(
            "flaky",
            "Flaky Orb",
            "Test Lab",
            "Orb",
            "test-1.0",
            "Mock");

        public DeviceConnectionState ConnectionState => DeviceConnectionState.Connected;

        public event EventHandler<DeviceConnectionStateChangedEventArgs>? ConnectionStateChanged
        {
            add { }
            remove { }
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<BatteryStatus> ReadBatteryAsync(
            CancellationToken cancellationToken = default)
        {
            var readNumber = Interlocked.Increment(ref _readCount);
            if (readNumber == 1)
            {
                return Task.FromException<BatteryStatus>(
                    new InvalidOperationException("Transient failure."));
            }

            return Task.FromResult(
                new BatteryStatus(42, false, DateTimeOffset.UtcNow));
        }

        public Task<IReadOnlyDictionary<string, string>> ReadParametersAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>());
        }

        public Task WriteParameterAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
