using VGNOrb.Core.Devices;

namespace VGNOrb.Core.Polling;

/// <summary>
/// Polls one connected device on an interval. It owns neither the device lifecycle
/// nor the UI; consumers subscribe to updates and stop polling when a device detaches.
/// </summary>
public sealed class DevicePollingService : IAsyncDisposable
{
    private readonly TimeSpan _interval;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _loop;
    private volatile bool _isRunning;
    private int _disposed;

    public DevicePollingService(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                interval,
                "Polling interval must be greater than zero.");
        }

        _interval = interval;
    }

    public event EventHandler<BatteryUpdatedEventArgs>? BatteryUpdated;

    public event EventHandler<PollingErrorEventArgs>? PollingFailed;

    public bool IsRunning => _isRunning;

    public async Task StartAsync(IDevice device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_loop is not null)
            {
                throw new InvalidOperationException("Polling is already running.");
            }

            var linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellationTokenSource = linkedCancellation;
            _loop = Task.Run(
                () => RunAsync(device, linkedCancellation.Token),
                CancellationToken.None);
            _isRunning = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_loop is null)
            {
                return;
            }

            _cancellationTokenSource?.Cancel();
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping.
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _loop = null;
                _isRunning = false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task RunAsync(IDevice device, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(_interval);
            while (true)
            {
                try
                {
                    var battery = await device.ReadBatteryAsync(cancellationToken).ConfigureAwait(false);
                    BatteryUpdated?.Invoke(this, new BatteryUpdatedEventArgs(battery));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    PollingFailed?.Invoke(this, new PollingErrorEventArgs(ex));
                }

                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }
}
