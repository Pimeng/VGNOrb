namespace VGNOrb.Core.Devices;

/// <summary>
/// A display-level snapshot persisted between app restarts, equivalent to the
/// "bat_&lt;addr&gt;" entries the VGN HUB web driver keeps in localStorage.
/// </summary>
public sealed record BatteryLevelSnapshot(int Level, DateTimeOffset SavedAt);

/// <summary>
/// Smooths raw battery readings into the display percentage, mirroring the
/// behavior of the VGN HUB web driver's Battery module:
/// <list type="bullet">
/// <item>Voltage-based level from a 21-point discharge curve when the device reports millivolts.</item>
/// <item>Slow display ramping: 1%/10s while charging (only when the actual level
/// leads by more than 10), 1%/60s trickle between 85 and 99 near full, 1%/10s
/// downwards while discharging.</item>
/// <item>Low-battery pin at 15% and a "full" latch that needs 8 consecutive
/// 100% readings while charging.</item>
/// <item>Restart continuity: a persisted snapshot is rate-limited against the
/// elapsed time (max +0.028%/s charge, -0.014%/s discharge).</item>
/// </list>
/// The reference implementation uses interval timers; this port advances the
/// display lazily from reading timestamps, which produces the same values at
/// poll time without owning any threads. All calls must come from one thread.
/// </summary>
public sealed class BatteryLevelSmoother
{
    // Observed VGN discharge curve: 21 breakpoints, 5 percentage points per segment.
    private static readonly int[] VoltageBreakpointsMillivolts =
    [
        3050, 3420, 3480, 3540, 3600, 3660, 3720, 3760, 3800, 3840, 3880,
        3920, 3940, 3960, 3980, 4000, 4020, 4040, 4060, 4080, 4110
    ];

    private static readonly TimeSpan ChargeTickInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TrickleTickInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan UnknownRestartGap = TimeSpan.FromSeconds(2000);

    private const int TrickleFloorPercent = 85;
    private const int TrickleCeilingPercent = 99;
    private const int TrickleActualFloorPercent = 95;
    private const int ChargeRampGapPercent = 10;
    private const int LowBatteryPinPercent = 15;
    private const int FullReadingsRequired = 8;
    private const int FullReadingCountCap = 10;

    private bool _isInitialized;
    private bool _hasVoltage;
    private bool _isCharging;
    private bool _lowBatteryPinned;
    private int _actualLevel;
    private int _displayLevel;
    private int _fullReadingCount;
    private DateTimeOffset _lastAdvanceAt;

    public bool IsInitialized => _isInitialized;

    /// <summary>The smoothed percentage shown to the user.</summary>
    public int DisplayLevel => _displayLevel;

    /// <summary>The current snapshot to persist for restart continuity.</summary>
    public BatteryLevelSnapshot Snapshot => new(_displayLevel, _lastAdvanceAt);

    /// <summary>
    /// Feeds one raw reading and returns the display percentage. On the first
    /// call the smoother initializes from the reading and, when available, a
    /// persisted snapshot from a previous run.
    /// </summary>
    public int Update(BatteryStatus reading, BatteryLevelSnapshot? persisted = null)
    {
        ArgumentNullException.ThrowIfNull(reading);

        if (!_isInitialized)
        {
            Initialize(reading, persisted);
        }

        var now = reading.ObservedAt;
        _isCharging = reading.IsCharging;
        _actualLevel = _hasVoltage
            ? VoltageToLevel(reading.VoltageMillivolts!.Value, reading.IsCharging)
            : reading.Percentage;

        AdvanceDisplay(now);
        ApplyLowBatteryAndFullRules(reading.Percentage);
        _displayLevel = Math.Clamp(_displayLevel, 0, 100);
        return _displayLevel;
    }

    /// <summary>
    /// Maps a battery voltage onto the observed discharge curve. Each segment
    /// spans 5 percentage points with linear interpolation inside the segment.
    /// </summary>
    public static int VoltageToLevel(int millivolts, bool isCharging)
    {
        // The reference web driver uses a strict ">" comparison here, which maps
        // exactly 4110 mV to 0; that boundary value is treated as full instead.
        if (millivolts >= VoltageBreakpointsMillivolts[^1])
        {
            return isCharging ? 99 : 100;
        }

        var index = -1;
        for (var i = 0; i < VoltageBreakpointsMillivolts.Length; i++)
        {
            if (millivolts < VoltageBreakpointsMillivolts[i])
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return 0;
        }

        double level;
        if (index == 0)
        {
            level = 0;
        }
        else
        {
            var segmentMillivolts =
                (VoltageBreakpointsMillivolts[index] - VoltageBreakpointsMillivolts[index - 1]) / 5d;
            level = (millivolts - VoltageBreakpointsMillivolts[index - 1]) / segmentMillivolts
                + (index - 1) * 5;
        }

        // Quirk carried over from the reference driver: a computed level of
        // exactly 0 or 15 is nudged up by one (0% is reserved for "no battery").
        if (level == 0 || level == 15)
        {
            level++;
        }

        return (int)Math.Round(level, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Blends a persisted display level with the freshly measured level after a
    /// restart. Gaps under a minute keep the persisted level; gaps over thirty
    /// minutes trust the measurement; in between the measurement is clamped to
    /// the physically plausible charge (+0.028%/s) and discharge (-0.014%/s)
    /// rates from the persisted level.
    /// </summary>
    public static int CalculateRestartLevel(int lastLevel, int actualLevel, TimeSpan elapsed)
    {
        var seconds = elapsed.TotalSeconds;
        if (seconds > 1800)
        {
            return actualLevel;
        }

        if (seconds < 60)
        {
            return lastLevel;
        }

        var max = Math.Min(100, 0.028 * seconds + lastLevel);
        var min = Math.Max(0, lastLevel - 0.014 * seconds);
        if (actualLevel > max)
        {
            return (int)Math.Round(max, MidpointRounding.AwayFromZero);
        }

        if (actualLevel < min)
        {
            return (int)Math.Round(min, MidpointRounding.AwayFromZero);
        }

        return lastLevel;
    }

    private void Initialize(BatteryStatus reading, BatteryLevelSnapshot? persisted)
    {
        _hasVoltage = reading.VoltageMillivolts is > 0;
        _isCharging = reading.IsCharging;
        _actualLevel = _hasVoltage
            ? VoltageToLevel(reading.VoltageMillivolts!.Value, reading.IsCharging)
            : reading.Percentage;

        var elapsed = persisted is null
            ? UnknownRestartGap
            : reading.ObservedAt - persisted.SavedAt;
        var lastLevel = persisted?.Level ?? 0;
        _displayLevel = CalculateRestartLevel(lastLevel, _actualLevel, elapsed);
        _lastAdvanceAt = reading.ObservedAt;
        _isInitialized = true;
    }

    /// <summary>
    /// Lazy equivalent of the reference driver's 10s/60s ramp timers. Time spent
    /// in an inactive regime does not accumulate, matching timers that keep
    /// firing but only act while their condition holds.
    /// </summary>
    private void AdvanceDisplay(DateTimeOffset now)
    {
        if (now < _lastAdvanceAt)
        {
            _lastAdvanceAt = now;
            return;
        }

        var remaining = now - _lastAdvanceAt;
        while (TryGetRampStep(out var tick, out var delta))
        {
            if (remaining < tick)
            {
                break;
            }

            _displayLevel += delta;
            remaining -= tick;
            _lastAdvanceAt += tick;
        }

        if (!TryGetRampStep(out _, out _))
        {
            _lastAdvanceAt = now;
        }
    }

    private bool TryGetRampStep(out TimeSpan tick, out int delta)
    {
        if (_isCharging)
        {
            if (_displayLevel >= TrickleFloorPercent && _actualLevel >= TrickleActualFloorPercent)
            {
                if (_displayLevel < TrickleCeilingPercent)
                {
                    tick = TrickleTickInterval;
                    delta = 1;
                    return true;
                }
            }
            else if (_actualLevel < 100 && _actualLevel - ChargeRampGapPercent > _displayLevel)
            {
                tick = ChargeTickInterval;
                delta = 1;
                return true;
            }
        }
        else if (_actualLevel < _displayLevel && _displayLevel > 0)
        {
            tick = ChargeTickInterval;
            delta = -1;
            return true;
        }

        tick = TimeSpan.Zero;
        delta = 0;
        return false;
    }

    private void ApplyLowBatteryAndFullRules(int rawLevel)
    {
        if (!_isCharging)
        {
            _fullReadingCount = 0;

            // 电量耗尽是确定性事件，跳过缓变直接显示 0%。
            if (rawLevel == 0)
            {
                _displayLevel = 0;
            }

            // 低电钉值（对应原驱动的 lowBattery 逻辑）：实测首次跌到 <=15% 且
            // 显示值还在 15% 以上时，把显示值一次性拉到 15%。因为显示电量经过
            // 缓变平滑、通常滞后于实测，不干预的话实测已经很低了界面却显示偏高，
            // UI 弹出低电告警时会对不上。只拉低这一次（_lowBatteryPinned），之后
            // 仍按 1%/10s 缓降；显示值已低于 15% 时不做任何事（不会往上拉回）。
            // 充电后标志清除。
            if (_displayLevel >= LowBatteryPinPercent
                && rawLevel <= LowBatteryPinPercent
                && !_lowBatteryPinned)
            {
                _lowBatteryPinned = true;
                _displayLevel = LowBatteryPinPercent;
            }
        }
        else
        {
            _lowBatteryPinned = false;

            // 满电锁存（对应原驱动的 fullBatteryCount 逻辑）：设备满电后实测值
            // 会在 99/100 附近波动，直接透传会让 UI 在 99% 和 100% 之间抖动。
            // 充电状态下连续 8 次读到 100% 才把显示值置为 100；计数上限 10 与
            // 原驱动一致，中途掉到 100 以下立即清零重新计数。
            if (rawLevel == 100)
            {
                if (_fullReadingCount < FullReadingCountCap)
                {
                    _fullReadingCount++;
                }

                if (_fullReadingCount == FullReadingsRequired)
                {
                    _displayLevel = 100;
                }
            }
            else
            {
                _fullReadingCount = 0;
            }
        }
    }
}
