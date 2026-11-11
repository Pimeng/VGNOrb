using System.Collections.Concurrent;
using VGNOrb.Core.Devices;

namespace VGNOrb.Avalonia.Services;

/// <summary>
/// Merges all supported hardware discovery sources and falls back to the mock only
/// when no hardware is available. The routing table keeps discovery and connection
/// semantics separate from the view model.
/// </summary>
public sealed class AutomaticDeviceDiscoveryService : IDeviceDiscoveryService
{
    private readonly IReadOnlyList<IDeviceDiscoveryService> _hardwareSources;
    private readonly IDeviceDiscoveryService _mock;
    private readonly ConcurrentDictionary<string, IDeviceDiscoveryService> _routes =
        new(StringComparer.Ordinal);

    public AutomaticDeviceDiscoveryService(
        IReadOnlyList<IDeviceDiscoveryService> hardwareSources,
        IDeviceDiscoveryService mock)
    {
        ArgumentNullException.ThrowIfNull(hardwareSources);
        if (hardwareSources.Count == 0)
        {
            throw new ArgumentException("At least one hardware source is required.", nameof(hardwareSources));
        }

        _hardwareSources = hardwareSources;
        _mock = mock ?? throw new ArgumentNullException(nameof(mock));
    }

    public async Task<IReadOnlyList<DeviceDescriptor>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        _routes.Clear();

        var devices = new List<DeviceDescriptor>();
        foreach (var source in _hardwareSources)
        {
            try
            {
                var discovered = await source.DiscoverAsync(cancellationToken).ConfigureAwait(false);
                foreach (var descriptor in discovered)
                {
                    if (_routes.TryAdd(descriptor.Id, source))
                    {
                        devices.Add(descriptor);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // A failing transport must not prevent other hardware sources or mock fallback.
            }
        }

        if (devices.Count == 0)
        {
            var mockDevices = await _mock.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            foreach (var descriptor in mockDevices)
            {
                if (_routes.TryAdd(descriptor.Id, _mock))
                {
                    devices.Add(descriptor);
                }
            }
        }

        return devices;
    }

    public Task<IDevice> ConnectAsync(
        DeviceDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (_routes.TryGetValue(descriptor.Id, out var discovery))
        {
            return discovery.ConnectAsync(descriptor, cancellationToken);
        }

        throw new InvalidOperationException("设备描述已过期，请重新发现设备。");
    }
}

