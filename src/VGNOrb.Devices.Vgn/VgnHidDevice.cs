using System.Collections.ObjectModel;
using HidSharp;
using VGNOrb.Core.Devices;

namespace VGNOrb.Devices.Vgn;

/// <summary>
/// Read-only adapter for the verified VGN F1 SE HID collection. The public VGN HUB
/// web driver was inspected to establish the report format; only online, battery,
/// and firmware queries are implemented. Configuration writes are intentionally
/// rejected until they have been independently verified.
/// </summary>
public sealed class VgnHidDevice : IDevice
{
    private readonly HidDevice _hidDevice;
    private readonly string _deviceId;
    private readonly string _productName;
    private readonly string _manufacturer;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private readonly SemaphoreSlim _batteryGate = new(1, 1);
    private readonly object _sync = new();
    private HidStream? _stream;
    private DeviceConnectionState _connectionState = DeviceConnectionState.Disconnected;
    private VgnBatteryReading? _lastBattery;
    private string _firmwareVersion = "未知";
    private int _disposed;

    public VgnHidDevice(HidDevice hidDevice)
    {
        _hidDevice = hidDevice ?? throw new ArgumentNullException(nameof(hidDevice));
        _productName = ReadHidString(hidDevice.GetProductName) ?? "VGN F1 SE";
        _manufacturer = ReadHidString(hidDevice.GetManufacturer) ?? "VGN";
        var serialNumber = ReadHidString(hidDevice.GetSerialNumber);
        _deviceId = string.IsNullOrWhiteSpace(serialNumber)
            ? $"vgn-f1se:{hidDevice.DevicePath}"
            : $"vgn-f1se:{serialNumber}";
    }

    public DeviceInfo Info => new(
        _deviceId,
        _productName,
        _manufacturer,
        _productName,
        _firmwareVersion,
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
                if (!_hidDevice.TryOpen(out var stream))
                {
                    throw new IOException("无法打开 VGN HID 设备；设备可能被其他程序独占。");
                }

                Volatile.Write(ref _stream, stream);
                stream.ReadTimeout = 1000;
                stream.WriteTimeout = 1000;

                var onlineReport = await SendCommandAsync(
                    VgnJmProtocol.DeviceOnlineCommand,
                    cancellationToken).ConfigureAwait(false);

                if (!VgnJmProtocol.TryParseDeviceOnline(onlineReport, out var online) || !online)
                {
                    throw new IOException("VGN 设备未上线。");
                }

                try
                {
                    var versionReport = await SendCommandAsync(
                        VgnJmProtocol.ReadVersionCommand,
                        cancellationToken).ConfigureAwait(false);

                    if (VgnJmProtocol.TryParseFirmwareVersion(versionReport, out var version))
                    {
                        _firmwareVersion = version;
                    }
                }
                catch (TimeoutException)
                {
                    // Firmware is informational; the verified battery path still works.
                }

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
            var onlineReport = await SendCommandAsync(
                VgnJmProtocol.DeviceOnlineCommand,
                cancellationToken).ConfigureAwait(false);

            if (!VgnJmProtocol.TryParseDeviceOnline(onlineReport, out var online) || !online)
            {
                SetState(DeviceConnectionState.Faulted);
                throw new IOException("VGN 设备离线。");
            }

            var batteryReport = await SendCommandAsync(
                VgnJmProtocol.BatteryLevelCommand,
                cancellationToken).ConfigureAwait(false);

            if (!VgnJmProtocol.TryParseBattery(batteryReport, out var reading))
            {
                SetState(DeviceConnectionState.Faulted);
                throw new InvalidDataException("VGN 电量响应格式无效。");
            }

            lock (_sync)
            {
                _lastBattery = reading;
            }

            return new BatteryStatus(
                reading.Percentage,
                reading.IsCharging,
                DateTimeOffset.UtcNow,
                reading.VoltageMillivolts);
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
            ["protocol"] = "VGN JM report 8",
            ["vendor.id"] = $"0x{_hidDevice.VendorID:X4}",
            ["product.id"] = $"0x{_hidDevice.ProductID:X4}"
        };

        lock (_sync)
        {
            if (_lastBattery is { } battery)
            {
                parameters["battery.voltage.mv"] = battery.VoltageMillivolts.ToString();
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
            "当前真实设备适配器只实现了经过验证的电量/在线读取；参数写入尚未验证。");
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

    private async Task<byte[]> SendCommandAsync(
        byte command,
        CancellationToken cancellationToken)
    {
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stream = Volatile.Read(ref _stream)
                ?? throw new IOException("VGN HID 数据流未打开。");

            return await Task.Run(
                () => SendCommandCore(stream, command, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private static byte[] SendCommandCore(
        HidStream stream,
        byte command,
        CancellationToken cancellationToken)
    {
        var packet = VgnJmProtocol.BuildCommandPacket(command);
        cancellationToken.ThrowIfCancellationRequested();
        stream.Write(packet);

        var buffer = new byte[VgnJmProtocol.ReportLength];
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

            if (read < VgnJmProtocol.ReportLength
                || !VgnJmProtocol.HasValidFrame(buffer)
                || buffer[1] != command)
            {
                continue;
            }

            if (buffer[2] != 0)
            {
                throw new IOException($"VGN 设备返回错误状态 0x{buffer[2]:X2}。");
            }

            return buffer;
        }

        throw new TimeoutException($"VGN HID 命令 0x{command:X2} 超时。");
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
