namespace VGNOrb.Core.Configuration;

public sealed record AppSettings
{
    public bool AlwaysOnTop { get; init; } = true;

    public bool AutoConnectDevice { get; init; } = true;

    public bool PreferHardware { get; init; } = true;

    public int PollIntervalMilliseconds { get; init; } = 5000;

    /// <summary>
    /// Clamps hand-edited values so a malformed configuration cannot create a
    /// polling storm.
    /// </summary>
    public AppSettings Normalize()
    {
        return this with
        {
            PollIntervalMilliseconds = Math.Clamp(PollIntervalMilliseconds, 1000, 60000)
        };
    }
}
