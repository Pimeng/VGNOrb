namespace VGNOrb.Core.Devices.Mock;

/// <summary>
/// Controls the deterministic simulated device. Delays are intentionally nonzero
/// by default so the UI exercises asynchronous loading states.
/// </summary>
public sealed record MockDeviceOptions
{
    public TimeSpan ConnectDelay { get; init; } = TimeSpan.FromMilliseconds(350);

    public TimeSpan DisconnectDelay { get; init; } = TimeSpan.FromMilliseconds(120);

    public TimeSpan ReadDelay { get; init; } = TimeSpan.FromMilliseconds(120);

    public int InitialBatteryPercentage { get; init; } = 86;

    public int BatteryDrainPerRead { get; init; } = 1;

    public int ChargeStepPerRead { get; init; } = 2;

    public int LowBatteryThreshold { get; init; } = 15;
}
