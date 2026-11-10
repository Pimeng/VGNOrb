using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VGNOrb.Core.Devices;

namespace VGNOrb.App.ViewModels;

/// <summary>
/// Owns the multi-device session: discovery, up to two independent device rows,
/// reconnect/refresh-all commands, and the overall tray status.
/// </summary>
public partial class OrbViewModel : ObservableObject, IAsyncDisposable
{
    public const int MaxDevices = 2;

    private readonly IDeviceDiscoveryService _discovery;
    private readonly Dispatcher _dispatcher;
    private readonly int _pollIntervalMilliseconds;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private int _disposed;

    public OrbViewModel(
        IDeviceDiscoveryService discovery,
        int pollIntervalMilliseconds,
        Dispatcher dispatcher)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _pollIntervalMilliseconds = pollIntervalMilliseconds;

        Devices.CollectionChanged += OnDevicesCollectionChanged;
    }

    public ObservableCollection<DeviceTileViewModel> Devices { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReconnectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectAllCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _alwaysOnTop = true;

    [ObservableProperty]
    private string _statusText = "准备就绪";

    public DeviceTileViewModel? PrimaryDevice => Devices.Count > 0 ? Devices[0] : null;

    public DeviceTileViewModel? SecondaryDevice => Devices.Count > 1 ? Devices[1] : null;

    public bool HasDevices => Devices.Count > 0;

    public bool IsSingleDevice => Devices.Count == 1;

    public bool IsDualDevice => Devices.Count >= 2;

    public double HudHeight => IsDualDevice ? 200 : 100;

    public int ConnectedDeviceCount => Devices.Count(device => device.IsConnected);

    public string DeviceCountText => $"{Devices.Count}/{MaxDevices}";

    public string TraySummary => Devices.Count == 0
        ? "未连接设备"
        : $"{Devices.Count} 台设备 · {ConnectedDeviceCount} 已连接";

    [RelayCommand(CanExecute = nameof(CanReconnectAll))]
    private async Task ReconnectAllAsync()
    {
        await _lifecycleGate.WaitAsync(_lifetimeCancellation.Token);
        SetBusy(true);

        try
        {
            await DisposeDevicesAsync();
            StatusText = "正在发现设备…";

            var discovered = await _discovery.DiscoverAsync(_lifetimeCancellation.Token);
            var descriptors = discovered
                .Where(descriptor => !string.IsNullOrWhiteSpace(descriptor.Id))
                .DistinctBy(descriptor => descriptor.Id, StringComparer.Ordinal)
                .Take(MaxDevices)
                .ToArray();

            if (descriptors.Length == 0)
            {
                StatusText = "未发现可用设备";
                return;
            }

            foreach (var descriptor in descriptors)
            {
                var tile = new DeviceTileViewModel(
                    descriptor,
                    _pollIntervalMilliseconds,
                    _dispatcher);
                Devices.Add(tile);
            }

            foreach (var tile in Devices.ToArray())
            {
                await ConnectTileAsync(tile, _lifetimeCancellation.Token);
            }

            UpdateSummary();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            StatusText = "操作已取消";
        }
        catch (Exception ex)
        {
            StatusText = $"设备发现失败：{ex.Message}";
        }
        finally
        {
            SetBusy(false);
            _lifecycleGate.Release();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshAll))]
    private async Task RefreshAllAsync()
    {
        await _lifecycleGate.WaitAsync(_lifetimeCancellation.Token);
        SetBusy(true);
        try
        {
            var connected = Devices.Where(device => device.IsConnected).ToArray();
            if (connected.Length == 0)
            {
                StatusText = "没有已连接设备";
                return;
            }

            await Task.WhenAll(connected.Select(device => device.RefreshAsync()));
            UpdateSummary();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            // Application shutdown.
        }
        catch (Exception ex)
        {
            StatusText = $"刷新失败：{ex.Message}";
        }
        finally
        {
            SetBusy(false);
            _lifecycleGate.Release();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDisconnectAll))]
    private async Task DisconnectAllAsync()
    {
        await _lifecycleGate.WaitAsync(_lifetimeCancellation.Token);
        SetBusy(true);
        try
        {
            await DisposeDevicesAsync();
            StatusText = "已断开全部设备";
        }
        catch (Exception ex)
        {
            StatusText = $"断开失败：{ex.Message}";
        }
        finally
        {
            SetBusy(false);
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetimeCancellation.Cancel();
        try
        {
            await _lifecycleGate.WaitAsync();
            try
            {
                await DisposeDevicesAsync();
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }
        finally
        {
            _lifetimeCancellation.Dispose();
            _lifecycleGate.Dispose();
        }
    }

    private async Task ConnectTileAsync(
        DeviceTileViewModel tile,
        CancellationToken cancellationToken)
    {
        IDevice? device = null;
        try
        {
            device = await _discovery.ConnectAsync(tile.Descriptor, cancellationToken);
            var attachedDevice = device;
            device = null;

            try
            {
                await tile.AttachAsync(attachedDevice, cancellationToken);
            }
            catch
            {
                await SafeDisposeDeviceAsync(attachedDevice).ConfigureAwait(true);
                throw;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (device is not null)
            {
                await SafeDisposeDeviceAsync(device).ConfigureAwait(true);
            }

            tile.MarkFailed($"连接失败：{ex.Message}");
        }
    }

    private async Task DisposeDevicesAsync()
    {
        var tiles = Devices.ToArray();
        Devices.Clear();

        foreach (var tile in tiles)
        {
            await tile.DisposeAsync();
        }

        NotifyCollectionStateChanged();
    }

    private void OnDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        NotifyCollectionStateChanged();
    }

    private void NotifyCollectionStateChanged()
    {
        OnPropertyChanged(nameof(PrimaryDevice));
        OnPropertyChanged(nameof(SecondaryDevice));
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(IsSingleDevice));
        OnPropertyChanged(nameof(IsDualDevice));
        OnPropertyChanged(nameof(HudHeight));
        OnPropertyChanged(nameof(DeviceCountText));
        OnPropertyChanged(nameof(ConnectedDeviceCount));
        OnPropertyChanged(nameof(TraySummary));
        ReconnectAllCommand.NotifyCanExecuteChanged();
        RefreshAllCommand.NotifyCanExecuteChanged();
        DisconnectAllCommand.NotifyCanExecuteChanged();
    }

    private void UpdateSummary()
    {
        var connected = ConnectedDeviceCount;
        if (Devices.Count == 0)
        {
            StatusText = "未发现可用设备";
        }
        else if (connected == 0)
        {
            StatusText = "所有设备均未连接";
        }
        else if (connected == Devices.Count)
        {
            StatusText = $"{connected} 台设备已连接";
        }
        else
        {
            StatusText = $"{connected}/{Devices.Count} 台设备已连接";
        }

        OnPropertyChanged(nameof(ConnectedDeviceCount));
        OnPropertyChanged(nameof(TraySummary));
        RefreshAllCommand.NotifyCanExecuteChanged();
        DisconnectAllCommand.NotifyCanExecuteChanged();
    }

    private bool CanReconnectAll() => !IsBusy;

    private bool CanRefreshAll() =>
        !IsBusy && Devices.Any(device => device.IsConnected);

    private bool CanDisconnectAll() => !IsBusy && Devices.Count > 0;

    private void SetBusy(bool value)
    {
        IsBusy = value;
    }

    private static async Task SafeDisposeDeviceAsync(IDevice device)
    {
        try
        {
            await device.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Disposal below is the important part.
        }

        try
        {
            await device.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // A failing adapter should not prevent the other tiles from working.
        }
    }
}
