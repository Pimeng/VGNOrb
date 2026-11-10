namespace VGNOrb.Core.Devices;

public sealed record BatteryStatus
{
    public BatteryStatus(
        int percentage,
        bool isCharging,
        DateTimeOffset observedAt,
        int? voltageMillivolts = null)
    {
        if (percentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(percentage),
                percentage,
                "Battery percentage must be between 0 and 100.");
        }

        if (voltageMillivolts is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(voltageMillivolts),
                voltageMillivolts,
                "Battery voltage must be positive when supplied.");
        }

        Percentage = percentage;
        IsCharging = isCharging;
        ObservedAt = observedAt;
        VoltageMillivolts = voltageMillivolts;
    }

    public int Percentage { get; }

    public bool IsCharging { get; }

    public DateTimeOffset ObservedAt { get; }

    public int? VoltageMillivolts { get; }
}
