using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using VGNOrb.Avalonia.ViewModels;
using VGNOrb.Avalonia.Views;
using Point = Avalonia.Point;

namespace VGNOrb.Avalonia.Services;

public sealed class DeviceHudController : IDisposable
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const double HudWindowBottomPadding = 16;
    private const double HotSpotSize = 20;
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(350);

    private readonly OrbViewModel _viewModel;
    private readonly DeviceHudWindow _hud;
    private readonly HotCornerWindow _hotCorner;
    private readonly DispatcherTimer _pollTimer;
    private DateTime? _hideAt;
    private bool _isHiding;
    private bool _disposed;

    public DeviceHudController(OrbViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        _hud = new DeviceHudWindow(viewModel)
        {
            ShowActivated = false,
            Topmost = viewModel.AlwaysOnTop
        };
        _hotCorner = new HotCornerWindow
        {
            ShowActivated = false
        };
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _pollTimer.Tick += OnPollTimerTick;

        _viewModel.Devices.CollectionChanged += OnDevicesCollectionChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _hud.Height = _viewModel.HudHeight + HudWindowBottomPadding;
    }

    public void Start()
    {
        ThrowIfDisposed();

        PositionWindows();
        _hud.Show();
        _hud.Hide();
        _hud.ResetAnimationState();

        _hotCorner.Show();
        PositionWindows();
        MakeClickThrough(_hud);
        MakeClickThrough(_hotCorner);
        _pollTimer.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pollTimer.Stop();
        _pollTimer.Tick -= OnPollTimerTick;
        _viewModel.Devices.CollectionChanged -= OnDevicesCollectionChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _hud.StopAnimation();
        _hud.Close();
        _hotCorner.Close();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OrbViewModel.AlwaysOnTop))
        {
            _hud.Topmost = _viewModel.AlwaysOnTop;
        }
    }

    private void OnDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _hud.Height = _viewModel.HudHeight + HudWindowBottomPadding;
        _hud.RefreshWindowHeight();
        _hud.UpdateContent();
        PositionWindows();
    }

    private void OnPollTimerTick(object? sender, EventArgs e)
    {
        var cursor = GetCursorPosition();
        var hotRect = GetHotSpotRectangle();
        var hudRect = GetWindowRectangle(_hud);
        var overHot = Contains(hotRect, cursor);
        var overHud = _hud.IsVisible && Contains(hudRect, cursor);

        if (overHot || overHud)
        {
            _hideAt = null;

            if (_isHiding)
            {
                _isHiding = false;
                _hud.StopAnimation();
                _hud.PlayShowAnimation();
            }
            else if (!_hud.IsVisible && overHot)
            {
                ShowHud();
            }

            return;
        }

        if (!_hud.IsVisible || _isHiding)
        {
            _hideAt = null;
            return;
        }

        _hideAt ??= DateTime.UtcNow + HideDelay;
        if (DateTime.UtcNow >= _hideAt)
        {
            _hideAt = null;
            BeginHide();
        }
    }

    private void ShowHud()
    {
        PositionWindows();
        _hud.Show();
        _hud.PlayShowAnimation();
    }

    private void BeginHide()
    {
        if (!_hud.IsVisible || _isHiding)
        {
            return;
        }

        _isHiding = true;
        _hud.PlayHideAnimation(() =>
        {
            if (!_isHiding)
            {
                return;
            }

            _isHiding = false;
            _hud.Hide();
            _hud.ResetAnimationState();
        });
    }

    private void PositionWindows()
    {
        var screen = _hud.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var scale = screen.Scaling;
        var hudLeft = screen.WorkingArea.Right - (int)Math.Ceiling(_hud.Width * scale);
        var hotLeft = screen.WorkingArea.Right - (int)Math.Ceiling(_hotCorner.Width * scale);

        _hud.Position = new PixelPoint(hudLeft, screen.WorkingArea.Y);
        _hotCorner.Position = new PixelPoint(hotLeft, screen.WorkingArea.Y);
    }

    private static void MakeClickThrough(Window window)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var extendedStyle = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(
            handle,
            GwlExStyle,
            extendedStyle | WsExTransparent | WsExToolWindow | WsExNoActivate);
    }

    private static bool Contains(Rect rect, Point point)
    {
        return rect.Width > 0
            && rect.Height > 0
            && point.X >= rect.Left
            && point.X <= rect.Right
            && point.Y >= rect.Top
            && point.Y <= rect.Bottom;
    }

    private static Point GetCursorPosition()
    {
        return GetCursorPos(out var point)
            ? new Point(point.X, point.Y)
            : new Point(double.NaN, double.NaN);
    }

    private Rect GetHotSpotRectangle()
    {
        var screen = _hotCorner.Screens.Primary;
        if (screen is null)
        {
            return GetWindowRectangle(_hotCorner);
        }

        var size = (int)Math.Ceiling(HotSpotSize * screen.Scaling);
        return new Rect(
            screen.WorkingArea.Right - size,
            screen.WorkingArea.Y,
            size,
            size);
    }

    private static Rect GetWindowRectangle(Window window)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
        {
            return default;
        }

        return new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr handle, int index, int newStyle);
}
