namespace VGNOrb.Core.Configuration;

public sealed record AppSettings
{
    public bool AlwaysOnTop { get; init; } = true;

    public bool ShowOrbOnStartup { get; init; } = true;

    public bool AutoConnectDevice { get; init; } = true;

    public bool PreferHardware { get; init; } = true;

    public double OrbSize { get; init; } = 116;

    public double? OrbLeft { get; init; }

    public double? OrbTop { get; init; }

    public int PollIntervalMilliseconds { get; init; } = 5000;

    /// <summary>
    /// Clamps hand-edited values so a malformed configuration cannot create an
    /// unusable orb or a polling storm.
    /// </summary>
    public AppSettings Normalize()
    {
        return this with
        {
            OrbSize = double.IsFinite(OrbSize) ? Math.Clamp(OrbSize, 72, 180) : 116,
            OrbLeft = NormalizeCoordinate(OrbLeft),
            OrbTop = NormalizeCoordinate(OrbTop),
            PollIntervalMilliseconds = Math.Clamp(PollIntervalMilliseconds, 1000, 60000)
        };
    }

    private static double? NormalizeCoordinate(double? value)
    {
        return value is { } coordinate && double.IsFinite(coordinate) ? coordinate : null;
    }
}
