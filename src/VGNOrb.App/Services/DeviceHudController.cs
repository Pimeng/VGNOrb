using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using VGNOrb.App.ViewModels;
using VGNOrb.App.Views;
using Point = System.Windows.Point;

namespace VGNOrb.App.Services;

/// <summary>
/// Reveals the top-right device HUD when the pointer enters the corner hot zone
/// and hides it after the pointer leaves both the hot zone and the HUD. Both
/// overlay windows are click-through so the corner does not block other apps.
/// </summary>
public sealed class DeviceHudController : IDisposable
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const double HudWindowBottomPadding = 16;
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

        _hud = new DeviceHudWindow
        {
            DataContext = viewModel
        };
        _hotCorner = new HotCornerWindow();
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _pollTimer.Tick += OnPollTimerTick;
        _viewModel.Devices.CollectionChanged += OnDevicesCollectionChanged;
        _hud.Height = _viewModel.HudHeight + HudWindowBottomPadding;
    }

    public void Start()
    {
        ThrowIfDisposed();

        // Initialize the hidden window without a visible flash so its HWND is
        // available for click-through styling and physical screen hit testing.
        PositionWindows();
        _hud.Opacity = 0;
        _hud.Show();
        _hud.Hide();
        _hud.Opacity = 1;
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
        _hud.StopAnimation();
        _hud.Close();
        _hotCorner.Close();
    }

    private void OnDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _hud.Height = _viewModel.HudHeight + HudWindowBottomPadding;
        PositionWindows();
    }

    private void OnPollTimerTick(object? sender, EventArgs e)
    {
        var cursor = GetCursorPosition();
        var hotRect = GetWindowRectangle(_hotCorner);
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
        var workArea = SystemParameters.WorkArea;

        _hotCorner.Left = workArea.Right - _hotCorner.Width;
        _hotCorner.Top = workArea.Top;

        _hud.Left = workArea.Right - _hud.Width;
        _hud.Top = workArea.Top;
    }

    private static void MakeClickThrough(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
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
        return !rect.IsEmpty
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

    private static Rect GetWindowRectangle(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
        {
            return Rect.Empty;
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
