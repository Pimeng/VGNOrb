using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VGNOrb.Avalonia.ViewModels;
using VGNOrb.Core.Configuration;

namespace VGNOrb.Avalonia.Views;

public sealed class DeviceHudWindow : Window
{
    private const double CollapsedWidth = 76;
    private const double CollapsedHeight = 48;
    private const double ExpandedWidth = 300;

    private readonly OrbViewModel _viewModel;
    private readonly Grid _root;
    private readonly Border _card;
    private readonly Grid _content;
    private readonly Canvas _singleCanvas;
    private readonly Canvas _dualCanvas;
    private readonly Grid _emptyState;
    private readonly DeviceRow _singleRow;
    private readonly DeviceRow _dualPrimaryRow;
    private readonly DeviceRow _dualSecondaryRow;
    private readonly DispatcherTimer _watchdogTimer;
    private readonly Stopwatch _animationClock = new();
    private readonly AutoResetEvent _frameClockWake = new(false);
    private readonly Action _frameTick;

    private Thread? _frameClockThread;
    private volatile bool _frameClockRunning;
    private int _framePending;
    private int _timerResolutionHeld;
    private long _lastFrameTimestamp;
    private volatile int _frameIntervalMilliseconds = 16;
    private bool _isAnimating;
    private bool _isHiding;
    private bool _verticalSync = true;
    private double _startWidth;
    private double _startHeight;
    private double _targetWidth;
    private double _targetHeight;
    private double _startRootOpacity;
    private double _startContentOpacity;
    private Action? _hideCompleted;

    public DeviceHudWindow(OrbViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        Title = "VGN Orb";
        Width = 320;
        Height = 116;
        CanResize = false;
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        Opacity = 1;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        IsHitTestVisible = false;

        _singleRow = new DeviceRow();
        _singleCanvas = new Canvas { Width = 300, Height = 100 };
        _singleCanvas.Children.Add(_singleRow);

        _dualPrimaryRow = new DeviceRow();
        _dualSecondaryRow = new DeviceRow();
        _dualCanvas = new Canvas { Width = 300, Height = 200 };
        Canvas.SetTop(_dualPrimaryRow, 0);
        Canvas.SetTop(_dualSecondaryRow, 100);
        _dualCanvas.Children.Add(_dualPrimaryRow);
        _dualCanvas.Children.Add(_dualSecondaryRow);

        _emptyState = new Grid
        {
            Width = 300,
            Height = 100,
            Children =
            {
                new TextBlock
                {
                    Text = "NO DEVICE",
                    Foreground = new SolidColorBrush(Color.Parse("#687868")),
                    FontFamily = new FontFamily("GeosansLight, Segoe UI"),
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };

        _content = new Grid
        {
            Opacity = 0,
            Children = { _singleCanvas, _dualCanvas, _emptyState }
        };

        _card = new Border
        {
            Width = ExpandedWidth,
            Height = 100,
            Background = Brushes.Black,
            ClipToBounds = true,
            Child = _content
        };

        _root = new Grid { Children = { _card } };
        Content = _root;
        ApplyCorner(_viewModel.HudCorner);

        // The spring is driven by a dedicated high resolution clock at the
        // display refresh rate. Neither a DispatcherTimer (WM_TIMER is quantized
        // to the 15.6 ms system tick) nor TopLevel.RequestAnimationFrame (which
        // spins synchronously with direct composition) can sample the elapsed
        // time curve often enough for a 120-200 Hz panel.
        _watchdogTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _watchdogTimer.Tick += OnWatchdogTick;
        _frameTick = OnFrameTick;
        Closed += (_, _) => StopAnimation();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Devices.CollectionChanged += OnDevicesCollectionChanged;
        UpdateContent();
    }

    public double WindowHeight => _viewModel.HudHeight + 16;

    /// <summary>
    /// Locks the spring sampling to the display refresh rate. With it disabled
    /// the frame clock runs as fast as it can, capped at
    /// <see cref="AppSettings.MaxSamplingRate"/> samples per second.
    /// </summary>
    public bool VerticalSync
    {
        get => _verticalSync;
        set
        {
            if (_verticalSync == value)
            {
                return;
            }

            _verticalSync = value;
            _frameIntervalMilliseconds = FrameIntervalMilliseconds();
        }
    }

    /// <summary>
    /// Anchors the card to the configured screen corner, rounds the corner that
    /// faces into the screen and mirrors the row for left-side placements.
    /// </summary>
    public void ApplyCorner(HudCorner corner)
    {
        _card.HorizontalAlignment = HudPlacement.HorizontalAlignmentFor(corner);
        _card.VerticalAlignment = HudPlacement.VerticalAlignmentFor(corner);
        _card.CornerRadius = HudPlacement.CardCornerRadius(corner);

        var mirrored = !HudPlacement.IsRightSide(corner);
        _singleRow.SetMirrored(mirrored);
        _dualPrimaryRow.SetMirrored(mirrored);
        _dualSecondaryRow.SetMirrored(mirrored);
    }

    public void PlayShowAnimation()
    {
        StopAnimation();

        _isHiding = false;
        _startWidth = _card.Width;
        _startHeight = _card.Height;
        _targetWidth = ExpandedWidth;
        _targetHeight = _viewModel.HudHeight;
        _startRootOpacity = _root.Opacity;
        _startContentOpacity = _content.Opacity;
        StartAnimation();
    }

    public void PlayHideAnimation(Action onCompleted)
    {
        ArgumentNullException.ThrowIfNull(onCompleted);
        StopAnimation();

        _isHiding = true;
        _startWidth = _card.Width;
        _startHeight = _card.Height;
        _targetWidth = CollapsedWidth;
        _targetHeight = CollapsedHeight;
        _startRootOpacity = _root.Opacity;
        _startContentOpacity = _content.Opacity;
        _hideCompleted = onCompleted;
        StartAnimation();
    }

    public void StopAnimation()
    {
        _isAnimating = false;
        Interlocked.Exchange(ref _framePending, 0);
        _frameClockRunning = false;
        _frameClockWake.Set();
        ReleaseHighResolutionTimer();
        _watchdogTimer.Stop();
        _animationClock.Stop();
        _hideCompleted = null;
    }

    public void ResetAnimationState()
    {
        StopAnimation();
        _card.Width = CollapsedWidth;
        _card.Height = CollapsedHeight;
        _content.Opacity = 0;
        _root.Opacity = 0;
    }

    public void UpdateContent()
    {
        _singleRow.Bind(_viewModel.PrimaryDevice);
        _dualPrimaryRow.Bind(_viewModel.PrimaryDevice);
        _dualSecondaryRow.Bind(_viewModel.SecondaryDevice);

        _singleCanvas.IsVisible = _viewModel.IsSingleDevice;
        _dualCanvas.IsVisible = _viewModel.IsDualDevice;
        _emptyState.IsVisible = !_viewModel.HasDevices;

        if (!_isAnimating && IsVisible)
        {
            _card.Height = _viewModel.HudHeight;
        }
    }

    public void RefreshWindowHeight()
    {
        Height = WindowHeight;
        if (!_isAnimating && IsVisible)
        {
            _card.Height = _viewModel.HudHeight;
        }
    }

    private const double ShowDurationMs = 600;
    private const double HideDurationMs = 230;
    private const double RootShowDurationMs = 80;
    private const double ContentShowDelayMs = 110;
    private const double ContentShowDurationMs = 220;
    private const double ContentHideDurationMs = 90;
    private const double RootHideDelayMs = 70;
    private const double RootHideDurationMs = 150;
    private const int VRefreshIndex = 116;

    private int FrameIntervalMilliseconds()
    {
        if (!VerticalSync)
        {
            return Math.Max(1, 1000 / AppSettings.MaxSamplingRate);
        }

        var refresh = ReadDisplayRefreshRate();
        var interval = Math.Clamp((int)Math.Round(1000.0 / refresh), 4, 16);
        return Math.Max(interval, 1000 / AppSettings.MaxSamplingRate);
    }

    /// <summary>
    /// VREFRESH of the primary display. The HUD always docks to the primary
    /// work area, so the primary refresh rate is the cadence to match.
    /// </summary>
    private static int ReadDisplayRefreshRate()
    {
        var deviceContext = GetDC(IntPtr.Zero);
        if (deviceContext == IntPtr.Zero)
        {
            return 60;
        }

        try
        {
            var refresh = GetDeviceCaps(deviceContext, VRefreshIndex);
            return refresh is >= 60 and <= 480 ? refresh : 60;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, deviceContext);
        }
    }

    private void AcquireHighResolutionTimer()
    {
        if (Interlocked.Exchange(ref _timerResolutionHeld, 1) == 0)
        {
            TimeBeginPeriod(1);
        }
    }

    private void ReleaseHighResolutionTimer()
    {
        if (Interlocked.Exchange(ref _timerResolutionHeld, 0) == 1)
        {
            TimeEndPeriod(1);
        }
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr deviceContext, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    private void StartAnimation()
    {
        _isAnimating = true;
        Interlocked.Exchange(ref _framePending, 0);
        _animationClock.Restart();
        _lastFrameTimestamp = Stopwatch.GetTimestamp();
        _watchdogTimer.Start();
        StartFrameClock();
        AdvanceAnimation();
    }

    private void StartFrameClock()
    {
        AcquireHighResolutionTimer();
        _frameIntervalMilliseconds = FrameIntervalMilliseconds();
        if (_frameClockRunning)
        {
            return;
        }

        _frameClockRunning = true;
        if (_frameClockThread is null)
        {
            _frameClockThread = new Thread(FrameClockLoop)
            {
                IsBackground = true,
                Name = "VGN Orb HUD frame clock"
            };
            _frameClockThread.Start();
        }
        else
        {
            _frameClockWake.Set();
        }
    }

    /// <summary>
    /// Posts one animation frame per display refresh interval and skips a beat
    /// while the UI thread is still busy with the previous one, so a slow frame
    /// can never queue up a backlog of stale spring positions.
    /// </summary>
    private void FrameClockLoop()
    {
        while (true)
        {
            if (!_frameClockRunning)
            {
                _frameClockWake.WaitOne();
                continue;
            }

            var start = Stopwatch.GetTimestamp();
            if (Interlocked.CompareExchange(ref _framePending, 1, 0) == 0)
            {
                Dispatcher.UIThread.Post(_frameTick, DispatcherPriority.Render);
            }

            var interval = _frameIntervalMilliseconds;
            var spentMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            var wait = (int)Math.Ceiling(interval - spentMilliseconds);
            if (wait > 0)
            {
                _frameClockWake.WaitOne(wait);
            }
        }
    }

    private void OnFrameTick()
    {
        Interlocked.Exchange(ref _framePending, 0);
        if (!_isAnimating)
        {
            return;
        }

        _lastFrameTimestamp = Stopwatch.GetTimestamp();
        AdvanceAnimation();
    }

    /// <summary>
    /// Safety net: the frame clock stops when the window is no longer composited
    /// (fully covered, session locked, display asleep). Letting the animation
    /// continue from the elapsed clock avoids a HUD frozen mid-spring or left
    /// half transparent when composition resumes.
    /// </summary>
    private void OnWatchdogTick(object? sender, EventArgs e)
    {
        if (!_isAnimating)
        {
            _watchdogTimer.Stop();
            return;
        }

        var stalledMilliseconds =
            (Stopwatch.GetTimestamp() - _lastFrameTimestamp) * 1000.0 / Stopwatch.Frequency;
        if (stalledMilliseconds < 150)
        {
            return;
        }

        Interlocked.Exchange(ref _framePending, 0);
        _lastFrameTimestamp = Stopwatch.GetTimestamp();
        AdvanceAnimation();
    }

    private void AdvanceAnimation()
    {
        var elapsed = _animationClock.Elapsed.TotalMilliseconds;

        if (_isHiding)
        {
            var sizeProgress = Math.Clamp(elapsed / HideDurationMs, 0, 1);
            var sizeEase = BackEaseIn(sizeProgress, 0.25);
            _card.Width = Lerp(_startWidth, _targetWidth, sizeEase);
            _card.Height = Lerp(_startHeight, _targetHeight, sizeEase);
            _content.Opacity = Lerp(
                _startContentOpacity,
                0,
                QuadraticIn(Math.Clamp(elapsed / ContentHideDurationMs, 0, 1)));
            _root.Opacity = elapsed < RootHideDelayMs
                ? _startRootOpacity
                : Lerp(
                    _startRootOpacity,
                    0,
                    QuadraticIn(Math.Clamp((elapsed - RootHideDelayMs) / RootHideDurationMs, 0, 1)));

            if (elapsed < HideDurationMs)
            {
                return;
            }

            _card.Width = _targetWidth;
            _card.Height = _targetHeight;
            _root.Opacity = 0;
            _content.Opacity = 0;
            var completed = _hideCompleted;
            StopAnimation();
            completed?.Invoke();
            return;
        }

        _card.Width = ShowSize(_startWidth, _targetWidth, elapsed, 7);
        _card.Height = ShowSize(_startHeight, _targetHeight, elapsed, 5);
        _root.Opacity = Lerp(
            _startRootOpacity,
            1,
            QuadraticOut(Math.Clamp(elapsed / RootShowDurationMs, 0, 1)));
        _content.Opacity = elapsed < ContentShowDelayMs
            ? _startContentOpacity
            : Lerp(
                _startContentOpacity,
                1,
                QuadraticOut(Math.Clamp((elapsed - ContentShowDelayMs) / ContentShowDurationMs, 0, 1)));

        if (elapsed < ShowDurationMs)
        {
            return;
        }

        _card.Width = _targetWidth;
        _card.Height = _targetHeight;
        _root.Opacity = 1;
        _content.Opacity = 1;
        StopAnimation();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        UpdateContent();
    }

    private void OnDevicesCollectionChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        UpdateContent();
    }

    private static double ShowSize(double start, double target, double elapsed, double overshoot)
    {
        var t = Math.Clamp(elapsed / ShowDurationMs, 0, 1);
        if (t <= 0)
        {
            return start;
        }

        if (t < 0.68)
        {
            return Lerp(
                start,
                target + overshoot,
                CubicBezier(t / 0.68, 0.15, 0.85, 0.3, 1));
        }

        if (t < 0.84)
        {
            return Lerp(
                target + overshoot,
                target - 2,
                CubicBezier((t - 0.68) / 0.16, 0.3, 0.7, 0.55, 1));
        }

        return Lerp(
            target - 2,
            target,
            CubicBezier((t - 0.84) / 0.16, 0.4, 0.6, 0.7, 1));
    }

    private static double CubicBezier(double x, double x1, double y1, double x2, double y2)
    {
        x = Math.Clamp(x, 0, 1);

        var low = 0d;
        var high = 1d;
        for (var i = 0; i < 24; i++)
        {
            var mid = (low + high) / 2;
            var sample = 3 * (1 - mid) * (1 - mid) * mid * x1
                + 3 * (1 - mid) * mid * mid * x2
                + mid * mid * mid;
            if (sample < x)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        var u = (low + high) / 2;
        return 3 * (1 - u) * (1 - u) * u * y1
            + 3 * (1 - u) * u * u * y2
            + u * u * u;
    }

    private static double QuadraticOut(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return 1 - (1 - t) * (1 - t);
    }

    private static double QuadraticIn(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t;
    }

    private static double BackEaseIn(double t, double amplitude)
    {
        t = Math.Clamp(t, 0, 1);
        var c3 = amplitude + 1;
        return c3 * t * t * t - amplitude * t * t;
    }

    private static double Lerp(double from, double to, double t)
    {
        return from + (to - from) * Math.Clamp(t, 0, 1);
    }
}
