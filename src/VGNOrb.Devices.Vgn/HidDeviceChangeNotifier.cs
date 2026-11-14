using HidSharp;
using VGNOrb.Core.Devices;

namespace VGNOrb.Devices.Vgn;

/// <summary>
/// Forwards HidSharp's device arrival/removal notifications so the UI can
/// re-run discovery when a cable or receiver is plugged in or removed.
/// Notifications are held back while Windows is still installing or starting
/// device drivers: opening the HID collections of a composite receiver in the
/// middle of its enumeration can leave its keyboard and mouse interfaces
/// unstarted.
/// </summary>
public sealed class HidDeviceChangeNotifier : IDeviceChangeNotifier, IDisposable
{
    private static readonly TimeSpan InstallPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxInstallWait = TimeSpan.FromSeconds(30);

    private int _disposed;
    private int _generation;

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
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var generation = Interlocked.Increment(ref _generation);
        _ = RaiseWhenSettledAsync(generation);
    }

    private async Task RaiseWhenSettledAsync(int generation)
    {
        var waited = TimeSpan.Zero;
        while (waited < MaxInstallWait && AreDriversBeingInstalled())
        {
            await Task.Delay(InstallPollInterval).ConfigureAwait(false);
            waited += InstallPollInterval;

            if (generation != Volatile.Read(ref _generation))
            {
                // A newer notification took over the wait.
                return;
            }
        }

        if (generation != Volatile.Read(ref _generation)
            || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool AreDriversBeingInstalled()
    {
        try
        {
            return DeviceList.Local.AreDriversBeingInstalled;
        }
        catch
        {
            return false;
        }
    }
}
