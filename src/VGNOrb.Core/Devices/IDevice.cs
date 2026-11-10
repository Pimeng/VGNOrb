namespace VGNOrb.Core.Devices;

public interface IDevice : IAsyncDisposable
{
    DeviceInfo Info { get; }

    DeviceConnectionState ConnectionState { get; }

    event EventHandler<DeviceConnectionStateChangedEventArgs>? ConnectionStateChanged;

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<BatteryStatus> ReadBatteryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> ReadParametersAsync(
        CancellationToken cancellationToken = default);

    Task WriteParameterAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default);
}
