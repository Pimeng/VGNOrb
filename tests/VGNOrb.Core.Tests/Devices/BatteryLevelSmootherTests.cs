using VGNOrb.Core.Devices;

namespace VGNOrb.Core.Tests.Devices;

public sealed class BatteryLevelSmootherTests
{
    private static readonly DateTimeOffset T0 = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(4200, false, 100)]
    [InlineData(4200, true, 99)]
    [InlineData(4110, false, 100)]
    [InlineData(3600, false, 20)]
    [InlineData(3720, false, 30)]
    [InlineData(3900, false, 53)]
    [InlineData(3050, false, 1)]
    [InlineData(3000, false, 1)]
    public void VoltageToLevel_FollowsObservedDischargeCurve(
        int millivolts,
        bool isCharging,
        int expected)
    {
        Assert.Equal(expected, BatteryLevelSmoother.VoltageToLevel(millivolts, isCharging));
    }

    [Fact]
    public void CalculateRestartLevel_LongGap_TrustsMeasurement()
    {
        var level = BatteryLevelSmoother.CalculateRestartLevel(
            50,
            80,
            TimeSpan.FromSeconds(1801));
        Assert.Equal(80, level);
    }

    [Fact]
    public void CalculateRestartLevel_ShortGap_KeepsPersistedLevel()
    {
        var level = BatteryLevelSmoother.CalculateRestartLevel(
            50,
            80,
            TimeSpan.FromSeconds(59));
        Assert.Equal(50, level);
    }

    [Theory]
    [InlineData(90, 67)]   // charge faster than 0.028%/s is clamped: 50 + 0.028*600
    [InlineData(30, 42)]   // discharge faster than 0.014%/s is clamped: 50 - 0.014*600
    [InlineData(55, 50)]   // within the plausible window the persisted level wins
    public void CalculateRestartLevel_MediumGap_RateLimitsMeasurement(
        int actualLevel,
        int expected)
    {
        var level = BatteryLevelSmoother.CalculateRestartLevel(
            50,
            actualLevel,
            TimeSpan.FromSeconds(600));
        Assert.Equal(expected, level);
    }

    [Fact]
    public void FirstReading_WithoutSnapshot_UsesMeasuredLevel()
    {
        var smoother = new BatteryLevelSmoother();

        var display = smoother.Update(Reading(55, charging: false, at: T0));

        Assert.Equal(55, display);
        Assert.True(smoother.IsInitialized);
        Assert.Equal(55, smoother.Snapshot.Level);
        Assert.Equal(T0, smoother.Snapshot.SavedAt);
    }

    [Fact]
    public void FirstReading_WithFreshSnapshot_KeepsPersistedLevel()
    {
        var smoother = new BatteryLevelSmoother();
        var persisted = new BatteryLevelSnapshot(42, T0 - TimeSpan.FromSeconds(30));

        var display = smoother.Update(Reading(55, charging: false, at: T0), persisted);

        Assert.Equal(42, display);
    }

    [Fact]
    public void Discharge_RampsDownOnePercentPerTenSeconds()
    {
        var smoother = new BatteryLevelSmoother();
        smoother.Update(Reading(50, charging: false, at: T0));

        var display = smoother.Update(
            Reading(30, charging: false, at: T0 + TimeSpan.FromSeconds(95)));

        Assert.Equal(41, display);
    }

    [Fact]
    public void Discharge_StopsAtActualLevel()
    {
        var smoother = new BatteryLevelSmoother();
        smoother.Update(Reading(50, charging: false, at: T0));

        var display = smoother.Update(
            Reading(48, charging: false, at: T0 + TimeSpan.FromMinutes(5)));

        Assert.Equal(48, display);
    }

    [Fact]
    public void Charging_RampsUpOnlyWhenActualLeadsByMoreThanTen()
    {
        var smoother = new BatteryLevelSmoother();
        smoother.Update(Reading(50, charging: true, at: T0));

        // A 5-point lead never triggers the ramp.
        var unchanged = smoother.Update(
            Reading(55, charging: true, at: T0 + TimeSpan.FromMinutes(5)));
        Assert.Equal(50, unchanged);

        // A 20-point lead closes to a 10-point lead at 1%/10s.
        var caughtUp = smoother.Update(
            Reading(70, charging: true, at: T0 + TimeSpan.FromMinutes(10)));
        Assert.Equal(60, caughtUp);
    }

    [Fact]
    public void ChargingNearFull_TricklesOnePercentPerMinuteUpTo99()
    {
        var smoother = new BatteryLevelSmoother();
        smoother.Update(Reading(97, charging: true, at: T0));

        var display = smoother.Update(
            Reading(97, charging: true, at: T0 + TimeSpan.FromMinutes(30)));

        Assert.Equal(99, display);
    }

    [Fact]
    public void Full_LatchesOnlyAfterEightConsecutiveFullReadings()
    {
        var smoother = new BatteryLevelSmoother();
        smoother.Update(Reading(96, charging: true, at: T0));

        var at = T0;
        var display = 0;
        for (var i = 0; i < 7; i++)
        {
            at += TimeSpan.FromSeconds(5);
            display = smoother.Update(Reading(100, charging: true, at));
            Assert.NotEqual(100, display);
        }

        at += TimeSpan.FromSeconds(5);
        display = smoother.Update(Reading(100, charging: true, at));
        Assert.Equal(100, display);
    }

    [Fact]
    public void Full_LatchResetsWhenReadingsDropBelow100()
    {
        var smoother = new BatteryLevelSmoother();
        smoother.Update(Reading(96, charging: true, at: T0));

        var at = T0;
        for (var i = 0; i < 7; i++)
        {
            at += TimeSpan.FromSeconds(5);
            smoother.Update(Reading(100, charging: true, at));
        }

        at += TimeSpan.FromSeconds(5);
        smoother.Update(Reading(99, charging: true, at));

        at += TimeSpan.FromSeconds(5);
        var display = smoother.Update(Reading(100, charging: true, at));
        Assert.NotEqual(100, display);
    }

    [Fact]
    public void LowBattery_PinsDisplayAtFifteen()
    {
        var smoother = new BatteryLevelSmoother();
        smoother.Update(Reading(40, charging: false, at: T0));

        var display = smoother.Update(
            Reading(10, charging: false, at: T0 + TimeSpan.FromSeconds(5)));

        Assert.Equal(15, display);
    }

    [Fact]
    public void EmptyBattery_ReportsZeroImmediately()
    {
        var smoother = new BatteryLevelSmoother();
        smoother.Update(Reading(40, charging: false, at: T0));

        var display = smoother.Update(
            Reading(0, charging: false, at: T0 + TimeSpan.FromSeconds(5)));

        Assert.Equal(0, display);
    }

    [Fact]
    public void VoltageReadings_SmoothFromDischargeCurveInsteadOfRawLevel()
    {
        var smoother = new BatteryLevelSmoother();
        // 3600 mV maps to 20% on the curve even though the device reports 40%.
        var display = smoother.Update(Reading(40, charging: false, at: T0, millivolts: 3600));

        Assert.Equal(20, display);
    }

    [Fact]
    public void InactiveRegime_DoesNotAccumulateRampTime()
    {
        var smoother = new BatteryLevelSmoother();
        smoother.Update(Reading(50, charging: false, at: T0));

        // Actual level stays equal to the display for an hour: no ramping.
        var display = smoother.Update(
            Reading(50, charging: false, at: T0 + TimeSpan.FromHours(1)));
        Assert.Equal(50, display);

        // Once the actual level drops, only elapsed time from that point counts.
        display = smoother.Update(
            Reading(40, charging: false, at: T0 + TimeSpan.FromHours(1) + TimeSpan.FromSeconds(25)));
        Assert.Equal(48, display);
    }

    private static BatteryStatus Reading(
        int percentage,
        bool charging,
        DateTimeOffset at,
        int? millivolts = null)
    {
        return new BatteryStatus(percentage, charging, at, millivolts);
    }
}
