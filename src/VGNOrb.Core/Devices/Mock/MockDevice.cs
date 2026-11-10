using System.Collections.ObjectModel;

namespace VGNOrb.Core.Devices.Mock;

/// <summary>
/// In-memory device used to exercise discovery, connection, polling and parameter
/// flows without any hardware. It never claims to speak a vendor protocol.
/// </summary>
public sealed class MockDevice : IDevice
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly MockDeviceOptions _options;
    private readonly Dictionary<string, string> _parameters;
    private DeviceConnectionState _connectionState = DeviceConnectionState.Disconnected;
    private int _batteryPercentage;
    private bool _isCharging;
    private int _disposed;

    public MockDevice(DeviceInfo info, MockDeviceOptions? options = null)
    {
        Info = info ?? throw new ArgumentNullException(nameof(info));
        _options = options ?? new MockDeviceOptions();
        ValidateOptions(_options);
        _batteryPercentage = _options.InitialBatteryPercentage;

        _parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["profile"] = "balanced",
            ["dpi"] = "1600",
            ["lighting.brightness"] = "70"
        };
    }

    public DeviceInfo Info { get; }

    public DeviceConnectionState ConnectionState
    {
        get
        {
            lock (_sync)
            {
                return _connectionState;
            }
        }
    }

    public event EventHandler<DeviceConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (ConnectionState == DeviceConnectionState.Connected)
            {
                return;
            }

            SetState(DeviceConnectionState.Connecting);
            try
            {
                await Task.Delay(_options.ConnectDelay, cancellationToken).ConfigureAwait(false);
                ThrowIfDisposed();
                SetState(DeviceConnectionState.Connected);
            }
            catch (OperationCanceledException)
            {
                SetState(DeviceConnectionState.Disconnected);
                throw;
            }
            catch
            {
                SetState(DeviceConnectionState.Faulted);
                throw;
            }
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previousState = ConnectionState;
            if (previousState is DeviceConnectionState.Disconnected or DeviceConnectionState.Disconnecting)
            {
                return;
            }

            SetState(DeviceConnectionState.Disconnecting);
            try
            {
                await Task.Delay(_options.DisconnectDelay, cancellationToken).ConfigureAwait(false);
                ThrowIfDisposed();
                SetState(DeviceConnectionState.Disconnected);
            }
            catch (OperationCanceledException)
            {
                SetState(previousState);
                throw;
            }
            catch
            {
                SetState(DeviceConnectionState.Faulted);
                throw;
            }
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task<BatteryStatus> ReadBatteryAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureConnected();
        await Task.Delay(_options.ReadDelay, cancellationToken).ConfigureAwait(false);
        ThrowIfDisposed();
        EnsureConnected();

        int percentage;
        bool isCharging;
        lock (_sync)
        {
            AdvanceSimulation();
            percentage = _batteryPercentage;
            isCharging = _isCharging;
        }

        return new BatteryStatus(percentage, isCharging, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyDictionary<string, string>> ReadParametersAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureConnected();
        await Task.Delay(_options.ReadDelay, cancellationToken).ConfigureAwait(false);
        ThrowIfDisposed();
        EnsureConnected();

        lock (_sync)
        {
            return new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(_parameters, StringComparer.OrdinalIgnoreCase));
        }
    }

    public async Task WriteParameterAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfDisposed();
        EnsureConnected();
        await Task.Delay(_options.ReadDelay, cancellationToken).ConfigureAwait(false);
        ThrowIfDisposed();
        EnsureConnected();

        lock (_sync)
        {
            _parameters[key] = value;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        SetState(DeviceConnectionState.Disconnected);
        _connectionGate.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void AdvanceSimulation()
    {
        if (_isCharging)
        {
            _batteryPercentage = Math.Min(100, _batteryPercentage + _options.ChargeStepPerRead);
            if (_batteryPercentage >= 100)
            {
                _isCharging = false;
            }

            return;
        }

        _batteryPercentage = Math.Max(0, _batteryPercentage - _options.BatteryDrainPerRead);
        if (_batteryPercentage <= _options.LowBatteryThreshold)
        {
            _isCharging = true;
        }
    }

    private void SetState(DeviceConnectionState nextState)
    {
        DeviceConnectionState previousState;
        lock (_sync)
        {
            previousState = _connectionState;
            if (previousState == nextState)
            {
                return;
            }

            _connectionState = nextState;
        }

        ConnectionStateChanged?.Invoke(
            this,
            new DeviceConnectionStateChangedEventArgs(previousState, nextState));
    }

    private void EnsureConnected()
    {
        if (ConnectionState != DeviceConnectionState.Connected)
        {
            throw new InvalidOperationException("The mock device is not connected.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private static void ValidateOptions(MockDeviceOptions options)
    {
        if (options.InitialBatteryPercentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Initial battery percentage must be between 0 and 100.");
        }

        if (options.LowBatteryThreshold is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Low battery threshold must be between 0 and 100.");
        }

        if (options.BatteryDrainPerRead < 0 || options.ChargeStepPerRead < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Battery step values cannot be negative.");
        }

        if (options.ConnectDelay < TimeSpan.Zero
            || options.DisconnectDelay < TimeSpan.Zero
            || options.ReadDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Simulation delays cannot be negative.");
        }
    }
}
