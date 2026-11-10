using VGNOrb.Core.Devices;

namespace VGNOrb.Core.Polling;

public sealed class BatteryUpdatedEventArgs(BatteryStatus battery) : EventArgs
{
    public BatteryStatus Battery { get; } = battery;
}
