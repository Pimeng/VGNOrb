using HidSharp;
using VGNOrb.Core.Devices;

namespace VGNOrb.Devices.Vgn;

/// <summary>
/// Forwards HidSharp's device arrival/removal notifications so the UI can
/// re-run discovery when a cable or receiver is plugged in or removed.
/// </summary>
public sealed class HidDeviceChangeNotifier : IDeviceChangeNotifier, IDisposable
{
    private int _disposed;

    public HidDeviceChangeNotifier()
    {
        DeviceList.Local.Changed += OnDeviceListChanged;
    }

    public event EventHandler? DevicesChanged;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        DeviceList.Local.Changed -= OnDeviceListChanged;
    }

    private void OnDeviceListChanged(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
