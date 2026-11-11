using System.Collections.ObjectModel;
using HidSharp;
using VGNOrb.Core.Devices;

namespace VGNOrb.Devices.Vgn;

/// <summary>
/// Shared HID lifecycle for the VGN protocol families that were mapped from the
/// official VGN HUB web driver but have not been validated against physical
/// hardware yet. Implements open/close, the query/response exchange and the
/// read-only <see cref="IDevice"/> surface; derived classes only describe their
/// wire format. Configuration writes are intentionally rejected.
/// </summary>
public abstract class VgnProtocolDeviceBase : IDevice
{
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private readonly SemaphoreSlim _batteryGate = new(1, 1);
    private readonly object _sync = new();
    private HidStream? _stream;
    private DeviceConnectionState _connectionState = DeviceConnectionState.Disconnected;
    private BatteryStatus? _lastBattery;
    private int _maxInputReportLength;
    private int _disposed;

    protected VgnProtocolDeviceBase(HidDevice hidDevice, string deviceIdPrefix)
    {
        HidDevice = hidDevice ?? throw new ArgumentNullException(nameof(hidDevice));
        ProductName = ReadHidString(hidDevice.GetProductName) ?? DefaultProductName;
        Manufacturer = ReadHidString(hidDevice.GetManufacturer) ?? "VGN";
        var serialNumber = ReadHidString(hidDevice.GetSerialNumber);
        DeviceId = string.IsNullOrWhiteSpace(serialNumber)
            ? $"{deviceIdPrefix}:{hidDevice.DevicePath}"
            : $"{deviceIdPrefix}:{serialNumber}";
    }

    protected HidDevice HidDevice { get; }

    protected string DeviceId { get; }

    protected string ProductName { get; }

    protected string Manufacturer { get; }

    /// <summary>Human-readable protocol family name shown in the parameters list.</summary>
    protected abstract string ProtocolName { get; }

    /// <summary>Fallback product name when the device reports no string.</summary>
    protected abstract string DefaultProductName { get; }

    public DeviceInfo Info => new(
        DeviceId,
        ProductName,
        Manufacturer,
        ProductName,
        "未知",
        "USB HID");

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
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                if (!HidDevice.TryOpen(out var stream))
                {
                    throw new IOException("无法打开 VGN HID 设备；设备可能被其他程序独占。");
                }

                stream.ReadTimeout = 1000;
                stream.WriteTimeout = 1000;
                _maxInputReportLength = Math.Max(HidDevice.GetMaxInputReportLength(), 8);
                Volatile.Write(ref _stream, stream);

                SetState(DeviceConnectionState.Connected);
            }
            catch (OperationCanceledException)
            {
                CloseStream();
                SetState(DeviceConnectionState.Disconnected);
                throw;
            }
            catch
            {
                CloseStream();
                SetState(DeviceConnectionState.Faulted);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (ConnectionState == DeviceConnectionState.Disconnected)
            {
                return;
            }

            SetState(DeviceConnectionState.Disconnecting);
            CloseStream();
            SetState(DeviceConnectionState.Disconnected);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<BatteryStatus> ReadBatteryAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (ConnectionState != DeviceConnectionState.Connected)
        {
            throw new InvalidOperationException("VGN 设备未连接。");
        }

        await _batteryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var report = await SendBatteryQueryAsync(cancellationToken).ConfigureAwait(false);

            lock (_sync)
            {
                _lastBattery = report;
            }

            return report;
        }
        catch (TimeoutException)
        {
            SetState(DeviceConnectionState.Faulted);
            throw;
        }
        catch (IOException)
        {
            SetState(DeviceConnectionState.Faulted);
            throw;
        }
        catch (InvalidDataException)
        {
            SetState(DeviceConnectionState.Faulted);
            throw;
        }
        finally
        {
            _batteryGate.Release();
        }
    }

    public Task<IReadOnlyDictionary<string, string>> ReadParametersAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["transport"] = "USB HID",
            ["protocol"] = ProtocolName,
            ["vendor.id"] = $"0x{HidDevice.VendorID:X4}",
            ["product.id"] = $"0x{HidDevice.ProductID:X4}"
        };

        lock (_sync)
        {
            if (_lastBattery is { } battery)
            {
                parameters["battery.charging"] = battery.IsCharging ? "true" : "false";
            }
        }

        IReadOnlyDictionary<string, string> result =
            new ReadOnlyDictionary<string, string>(parameters);
        return Task.FromResult(result);
    }

    public Task WriteParameterAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfDisposed();

        throw new NotSupportedException(
            "当前设备适配器只实现了电量读取；参数写入尚未验证。");
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        CloseStream();
        SetState(DeviceConnectionState.Disconnected);
        _lifecycleGate.Dispose();
        _ioGate.Dispose();
        _batteryGate.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Builds the battery query output report. <paramref name="maxOutputReportLength"/>
    /// is the length the HID stack expects, including the report ID byte.
    /// </summary>
    protected abstract byte[] BuildBatteryReport(int maxOutputReportLength);

    /// <summary>
    /// Validates a raw input report and, when it answers the battery query,
    /// extracts the reading. Must return false for unrelated reports.
    /// </summary>
    protected abstract bool TryParseBatteryReport(
        ReadOnlySpan<byte> report,
        out BatteryStatus battery);

    private async Task<BatteryStatus> SendBatteryQueryAsync(CancellationToken cancellationToken)
    {
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stream = Volatile.Read(ref _stream)
                ?? throw new IOException("VGN HID 数据流未打开。");

            return await Task.Run(
                () => SendBatteryQueryCore(stream, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private BatteryStatus SendBatteryQueryCore(HidStream stream, CancellationToken cancellationToken)
    {
        var report = BuildBatteryReport(SafeGetMaxOutputReportLength());
        cancellationToken.ThrowIfCancellationRequested();
        stream.Write(report);

        var buffer = new byte[_maxInputReportLength];
        for (var attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read;
            try
            {
                read = stream.Read(buffer, 0, buffer.Length);
            }
            catch (TimeoutException)
            {
                continue;
            }

            if (TryParseBatteryReport(buffer.AsSpan(0, read), out var battery))
            {
                return battery;
            }
        }

        throw new TimeoutException($"{ProtocolName} 电量查询超时。");
    }

    private int SafeGetMaxOutputReportLength()
    {
        try
        {
            return HidDevice.GetMaxOutputReportLength();
        }
        catch
        {
            return 65;
        }
    }

    private void CloseStream()
    {
        var stream = Interlocked.Exchange(ref _stream, null);
        stream?.Dispose();
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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private static string? ReadHidString(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }
}
