using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VGNOrb.Core.Devices;
using VGNOrb.Core.Polling;

namespace VGNOrb.App.ViewModels;

/// <summary>
/// One independent device tile. Each tile owns its polling loop and device handle,
/// so one device failing or disconnecting does not affect the other tiles.
/// </summary>
public partial class DeviceTileViewModel : ObservableObject, IAsyncDisposable
{
    private readonly DevicePollingService _polling;
    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private IDevice? _device;
    private int _disposed;

    public DeviceTileViewModel(
        DeviceDescriptor descriptor,
        int pollIntervalMilliseconds,
        Dispatcher dispatcher)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _polling = new DevicePollingService(
            TimeSpan.FromMilliseconds(pollIntervalMilliseconds));

        DeviceName = descriptor.DisplayName;
        HudName = BuildHudName(descriptor);
        DeviceDetails = "等待连接 · USB HID";
        ConnectionText = "连接中…";
        StatusText = "正在连接设备…";

        _polling.BatteryUpdated += OnBatteryUpdated;
        _polling.PollingFailed += OnPollingFailed;
    }

    public DeviceDescriptor Descriptor { get; }

    public string Id => Descriptor.Id;

    public DeviceKind Kind => Descriptor.Kind;

    public string HudName { get; }

    public string KindLabel => Kind switch
    {
        DeviceKind.Mouse => "鼠标",
        DeviceKind.Keyboard => "键盘",
        DeviceKind.Simulated => "模拟设备",
        _ => "设备"
    };

    public string IdentifyText => Kind switch
    {
        DeviceKind.Mouse => "Identify: Mouse",
        DeviceKind.Keyboard => "Identify: Keyboard",
        DeviceKind.Simulated => "Identify: Simulated",
        _ => "Identify: Device"
    };

    public bool IsSimulated => Kind == DeviceKind.Simulated;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatteryText))]
    [NotifyPropertyChangedFor(nameof(BatteryDetailedText))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatteryText))]
    [NotifyPropertyChangedFor(nameof(BatteryDetailedText))]
    private int _batteryPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatteryDetailedText))]
    private bool _isCharging;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatteryDetailedText))]
    private int? _batteryVoltageMillivolts;

    [ObservableProperty]
    private string _deviceName;

    [ObservableProperty]
    private string _deviceDetails;

    [ObservableProperty]
    private string _connectionText;

    [ObservableProperty]
    private string _statusText;

    [ObservableProperty]
    private string _parameterText = "参数：--";

    [ObservableProperty]
    private string _lastUpdatedText = "尚未读取";

    public string BatteryText => IsConnected ? $"{BatteryPercent}%" : "--";

    public string BatteryDetailedText
    {
        get
        {
            if (!IsConnected)
            {
                return "电量：--";
            }

            var text = IsCharging
                ? $"电量：{BatteryPercent}%（充电中）"
                : $"电量：{BatteryPercent}%";

            if (BatteryVoltageMillivolts is > 0)
            {
                text += $" · {BatteryVoltageMillivolts.Value / 1000d:0.00} V";
            }

            return text;
        }
    }

    public async Task AttachAsync(IDevice device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ThrowIfDisposed();

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_device is not null)
            {
                throw new InvalidOperationException("设备已经绑定到该悬浮球。");
            }

            device.ConnectionStateChanged += OnDeviceConnectionStateChanged;
            _device = device;
            DeviceName = device.Info.DisplayName;
            DeviceDetails =
                $"{device.Info.Summary} · FW {device.Info.FirmwareVersion} · {device.Info.Transport}";
            IsConnected = true;
            ConnectionText = "已连接";

            try
            {
                await _polling.StartAsync(device, cancellationToken);

                var parameters = await device.ReadParametersAsync(cancellationToken);
                ParameterText = BuildParameterText(parameters);
                StatusText = BuildConnectedStatus(isCharging: false);
            }
            catch
            {
                device.ConnectionStateChanged -= OnDeviceConnectionStateChanged;
                if (ReferenceEquals(_device, device))
                {
                    _device = null;
                }

                IsConnected = false;
                ConnectionText = "连接失败";
                await _polling.StopAsync();
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void MarkFailed(string message)
    {
        IsConnected = false;
        ConnectionText = "连接失败";
        StatusText = message;
    }

    public async Task RefreshAsync()
    {
        var device = _device;
        if (device is null || !IsConnected)
        {
            return;
        }

        try
        {
            var battery = await device.ReadBatteryAsync(_lifetimeCancellation.Token);
            ApplyBattery(battery);

            var parameters = await device.ReadParametersAsync(_lifetimeCancellation.Token);
            ParameterText = BuildParameterText(parameters);
            StatusText = IsSimulated
                ? "已刷新 · 模拟数据"
                : $"已刷新 · {KindLabel}";
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Application shutdown.
        }
        catch (Exception ex)
        {
            StatusText = $"刷新失败：{ex.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetimeCancellation.Cancel();
        _polling.BatteryUpdated -= OnBatteryUpdated;
        _polling.PollingFailed -= OnPollingFailed;

        try
        {
            await _lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await _polling.StopAsync().ConfigureAwait(false);

                var device = _device;
                _device = null;
                if (device is not null)
                {
                    device.ConnectionStateChanged -= OnDeviceConnectionStateChanged;
                    try
                    {
                        await device.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // The device handle is disposed below.
                    }

                    try
                    {
                        await device.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // A failing adapter should not prevent app shutdown.
                    }
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }
        finally
        {
            await _polling.DisposeAsync().ConfigureAwait(false);
            _lifetimeCancellation.Dispose();
            _lifecycleGate.Dispose();
        }
    }

    private void OnBatteryUpdated(object? sender, BatteryUpdatedEventArgs e)
    {
        PostToUi(() =>
        {
            if (IsConnected)
            {
                ApplyBattery(e.Battery);
            }
        });
    }

    private void OnPollingFailed(object? sender, PollingErrorEventArgs e)
    {
        PostToUi(() =>
        {
            if (IsConnected)
            {
                StatusText = $"读取失败：{e.Error.Message}";
            }
        });
    }

    private void OnDeviceConnectionStateChanged(
        object? sender,
        DeviceConnectionStateChangedEventArgs e)
    {
        if (e.CurrentState == DeviceConnectionState.Faulted)
        {
            PostToUi(() =>
            {
                if (IsConnected)
                {
                    ConnectionText = "设备异常";
                    StatusText = "设备进入异常状态，请重新发现设备。";
                }
            });
        }
    }

    private void ApplyBattery(BatteryStatus battery)
    {
        BatteryPercent = battery.Percentage;
        IsCharging = battery.IsCharging;
        BatteryVoltageMillivolts = battery.VoltageMillivolts;
        LastUpdatedText = $"更新于 {battery.ObservedAt.ToLocalTime():HH:mm:ss}";
        StatusText = BuildConnectedStatus(battery.IsCharging);
    }

    private string BuildConnectedStatus(bool isCharging)
    {
        if (IsSimulated)
        {
            return isCharging ? "已连接 · 充电中（模拟）" : "已连接 · 模拟数据";
        }

        return isCharging
            ? $"已连接 · {KindLabel} · 充电中"
            : $"已连接 · {KindLabel}";
    }

    private void PostToUi(Action action)
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            _dispatcher.BeginInvoke(DispatcherPriority.DataBind, action);
        }
        catch (TaskCanceledException)
        {
            // Dispatcher is shutting down.
        }
        catch (InvalidOperationException)
        {
            // Dispatcher is shutting down.
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private static string BuildHudName(DeviceDescriptor descriptor)
    {
        if (descriptor.Kind == DeviceKind.Simulated)
        {
            return "VGN Orb";
        }

        if (descriptor.DisplayName.Contains("F1 SE", StringComparison.OrdinalIgnoreCase))
        {
            return "VGN F1 SE";
        }

        if (descriptor.DisplayName.Contains("V98Pro", StringComparison.OrdinalIgnoreCase))
        {
            return descriptor.DisplayName.Contains("TIMI", StringComparison.OrdinalIgnoreCase)
                ? "VGN V98Pro V4 TIMI"
                : "VGN V98Pro V4";
        }

        return descriptor.DisplayName;
    }

    private static string BuildParameterText(IReadOnlyDictionary<string, string> parameters)
    {
        if (parameters.Count == 0)
        {
            return "参数：--";
        }

        if (parameters.TryGetValue("transport", out var transport))
        {
            var voltage = parameters.TryGetValue("battery.voltage.mv", out var millivolts)
                ? $" · {millivolts} mV"
                : string.Empty;
            return $"参数：{transport}{voltage}";
        }

        var profile = parameters.TryGetValue("profile", out var profileValue)
            ? profileValue
            : "--";
        var dpi = parameters.TryGetValue("dpi", out var dpiValue)
            ? $"{dpiValue} DPI"
            : "--";
        return $"参数：{profile} · {dpi}";
    }
}
