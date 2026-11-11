using System.Text.Json.Serialization;

namespace VGNOrb.Core.Configuration;

public sealed record AppSettings
{
    public bool AlwaysOnTop { get; init; } = true;

    public bool AutoConnectDevice { get; init; } = true;

    public bool PreferHardware { get; init; } = true;

    public int PollIntervalMilliseconds { get; init; } = 5000;

    [JsonConverter(typeof(JsonStringEnumConverter<HudCorner>))]
    public HudCorner HudCorner { get; init; } = HudCorner.TopRight;

    /// <summary>
    /// Clamps hand-edited values so a malformed configuration cannot create a
    /// polling storm or leave the HUD in an undefined corner.
    /// </summary>
    public AppSettings Normalize()
    {
        return this with
        {
            PollIntervalMilliseconds = Math.Clamp(PollIntervalMilliseconds, 1000, 60000),
            HudCorner = Enum.IsDefined(HudCorner) ? HudCorner : HudCorner.TopRight
        };
    }
}
