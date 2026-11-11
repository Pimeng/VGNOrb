using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VGNOrb.Avalonia.ViewModels;

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
    private readonly DispatcherTimer _animationTimer;
    private readonly Stopwatch _animationClock = new();

    private bool _isAnimating;
    private bool _isHiding;
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
            CornerRadius = new CornerRadius(0, 0, 0, 30),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Child = _content
        };

        _root = new Grid { Children = { _card } };
        Content = _root;

        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _animationTimer.Tick += OnAnimationTick;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Devices.CollectionChanged += OnDevicesCollectionChanged;
        UpdateContent();
    }

    public double WindowHeight => _viewModel.HudHeight + 16;

    public void PlayShowAnimation()
    {
        StopAnimation();

        _isHiding = false;
        _isAnimating = true;
        _startWidth = _card.Width;
        _startHeight = _card.Height;
        _targetWidth = ExpandedWidth;
        _targetHeight = _viewModel.HudHeight;
        _startRootOpacity = _root.Opacity;
        _startContentOpacity = _content.Opacity;
        _animationClock.Restart();
        _animationTimer.Start();
    }

    public void PlayHideAnimation(Action onCompleted)
    {
        ArgumentNullException.ThrowIfNull(onCompleted);
        StopAnimation();

        _isHiding = true;
        _isAnimating = true;
        _startWidth = _card.Width;
        _startHeight = _card.Height;
        _targetWidth = CollapsedWidth;
        _targetHeight = CollapsedHeight;
        _startRootOpacity = _root.Opacity;
        _startContentOpacity = _content.Opacity;
        _hideCompleted = onCompleted;
        _animationClock.Restart();
        _animationTimer.Start();
    }

    public void StopAnimation()
    {
        _animationTimer.Stop();
        _animationClock.Stop();
        _isAnimating = false;
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

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        var duration = _isHiding ? 230d : 600d;
        var t = Math.Clamp(_animationClock.Elapsed.TotalMilliseconds / duration, 0, 1);

        if (_isHiding)
        {
            var eased = t * t * t;
            _card.Width = Lerp(_startWidth, _targetWidth, eased);
            _card.Height = Lerp(_startHeight, _targetHeight, eased);
            _content.Opacity = Lerp(_startContentOpacity, 0, Math.Clamp(t / 0.4, 0, 1));
            _root.Opacity = t < 0.25
                ? _startRootOpacity
                : Lerp(_startRootOpacity, 0, SmoothStep((t - 0.25) / 0.75));
        }
        else
        {
            _card.Width = ShowSize(_startWidth, _targetWidth, t, 7);
            _card.Height = ShowSize(_startHeight, _targetHeight, t, 5);
            _root.Opacity = Lerp(_startRootOpacity, 1, Math.Clamp(t / 0.13, 0, 1));
            _content.Opacity = t < 0.1
                ? _startContentOpacity
                : Lerp(_startContentOpacity, 1, SmoothStep(Math.Clamp((t - 0.1) / 0.35, 0, 1)));
        }

        if (t < 1)
        {
            return;
        }

        _card.Width = _targetWidth;
        _card.Height = _targetHeight;
        if (_isHiding)
        {
            _root.Opacity = 0;
            _content.Opacity = 0;
            var completed = _hideCompleted;
            _animationTimer.Stop();
            _isAnimating = false;
            _hideCompleted = null;
            completed?.Invoke();
        }
        else
        {
            _root.Opacity = 1;
            _content.Opacity = 1;
            _animationTimer.Stop();
            _isAnimating = false;
        }
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

    private static double ShowSize(double start, double target, double t, double overshoot)
    {
        if (t <= 0)
        {
            return start;
        }

        if (t < 0.68)
        {
            return Lerp(start, target + overshoot, SmoothStep(t / 0.68));
        }

        if (t < 0.84)
        {
            return Lerp(target + overshoot, target - 2, SmoothStep((t - 0.68) / 0.16));
        }

        return Lerp(target - 2, target, SmoothStep((t - 0.84) / 0.16));
    }

    private static double SmoothStep(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double Lerp(double from, double to, double t)
    {
        return from + (to - from) * Math.Clamp(t, 0, 1);
    }
}
