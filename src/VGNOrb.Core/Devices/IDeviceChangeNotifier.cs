namespace VGNOrb.Core.Devices;

/// <summary>
/// Raised when the operating system reports that a device was plugged in or
/// removed. The event carries no detail: listeners re-run discovery to find out
/// what changed. It may fire on any thread and in bursts.
/// </summary>
public interface IDeviceChangeNotifier
{
    event EventHandler? DevicesChanged;
}
