namespace VGNOrb.Core.Devices;

public enum DeviceConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
    Faulted
}

public sealed class DeviceConnectionStateChangedEventArgs(
    DeviceConnectionState previousState,
    DeviceConnectionState currentState) : EventArgs
{
    public DeviceConnectionState PreviousState { get; } = previousState;

    public DeviceConnectionState CurrentState { get; } = currentState;
}
