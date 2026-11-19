using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Avalonia;

namespace VGNOrb.Avalonia.Services;

/// <summary>
/// Lightweight Win32 Shell_NotifyIconW host that gives full control over
/// right-click events so VGN Orb can present its custom Fluent tray menu.
/// </summary>
internal sealed class Win32TrayIcon : IDisposable
{
    private const uint WmUser = 0x0400;
    private const uint WmTrayCallback = WmUser + 0x101;
    private const uint WmContextMenu = 0x007B;
    private const uint WmDestroy = 0x0002;

    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NotifyIconVersion4 = 4;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifShowTip = 0x00000080;

    private const int SmCxSmIcon = 49;
    private const int SmCySmIcon = 50;
    private const uint IconResourceVersion = 0x00030000;

    private readonly string _className;
    private readonly WndProcDelegate _wndProc;
    private readonly uint _taskbarCreatedMessage;
    private readonly nint _hInstance;
    private nint _hwnd;
    private nint _hIcon;
    private string _toolTipText = string.Empty;
    private bool _iconAdded;
    private bool _disposed;

    public Win32TrayIcon(byte[] icoBytes, string toolTipText)
    {
        ArgumentNullException.ThrowIfNull(icoBytes);

        _toolTipText = TruncateTip(toolTipText);
        _hInstance = GetModuleHandleW(null);
        _className = $"VGNOrbTrayHost_{Guid.NewGuid():N}";
        _wndProc = WindowProc;
        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");

        var wndClass = new WndClassExW
        {
            cbSize = (uint)Marshal.SizeOf<WndClassExW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _hInstance,
            lpszClassName = _className
        };

        if (RegisterClassExW(ref wndClass) == 0)
        {
            throw new InvalidOperationException($"RegisterClassExW failed ({Marshal.GetLastWin32Error()}).");
        }

        // Use a hidden top-level window (not HWND_MESSAGE) so explorer.exe's
        // TaskbarCreated broadcast reaches us after a shell restart.
        _hwnd = CreateWindowExW(
            0,
            _className,
            "VGNOrbTrayHost",
            0,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            _hInstance,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            UnregisterClassW(_className, _hInstance);
            throw new InvalidOperationException($"CreateWindowExW failed ({Marshal.GetLastWin32Error()}).");
        }

        _hIcon = CreateSmallIconFromIco(icoBytes);
        AddIcon();
    }

    public event Action<PixelPoint>? ContextMenuRequested;

    public string ToolTipText
    {
        get => _toolTipText;
        set
        {
            if (_disposed)
            {
                return;
            }

            var normalized = TruncateTip(value);
            if (string.Equals(_toolTipText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _toolTipText = normalized;
            if (_iconAdded)
            {
                var data = CreateNotifyData(NifTip | NifShowTip);
                Shell_NotifyIconW(NimModify, ref data);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_iconAdded && _hwnd != IntPtr.Zero)
        {
            var data = CreateNotifyData(0);
            Shell_NotifyIconW(NimDelete, ref data);
            _iconAdded = false;
        }

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        UnregisterClassW(_className, _hInstance);

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }

    private void AddIcon()
    {
        if (_disposed || _hwnd == IntPtr.Zero)
        {
            return;
        }

        var data = CreateNotifyData(NifMessage | NifIcon | NifTip | NifShowTip);
        if (Shell_NotifyIconW(NimAdd, ref data))
        {
            _iconAdded = true;
            data.uTimeoutOrVersion = NotifyIconVersion4;
            Shell_NotifyIconW(NimSetVersion, ref data);
        }
    }

    private NotifyIconDataW CreateNotifyData(uint flags)
    {
        return new NotifyIconDataW
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconDataW>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = WmTrayCallback,
            hIcon = _hIcon,
            szTip = _toolTipText,
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
    }

    private nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmTrayCallback)
        {
            var eventId = (uint)((long)lParam & 0xFFFF);
            if (eventId == WmContextMenu)
            {
                if (GetCursorPos(out var cursor))
                {
                    // SetForegroundWindow ensures that clicking outside our
                    // Avalonia popup menu reliably deactivates and dismisses it.
                    SetForegroundWindow(hwnd);
                    ContextMenuRequested?.Invoke(new PixelPoint(cursor.X, cursor.Y));
                }

                return IntPtr.Zero;
            }
        }
        else if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
        {
            _iconAdded = false;
            AddIcon();
            return IntPtr.Zero;
        }
        else if (msg == WmDestroy)
        {
            return IntPtr.Zero;
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static nint CreateSmallIconFromIco(byte[] icoBytes)
    {
        if (icoBytes.Length < 6)
        {
            return IntPtr.Zero;
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(icoBytes.AsSpan(4, 2));
        if (count == 0 || icoBytes.Length < 6 + count * 16)
        {
            return IntPtr.Zero;
        }

        var targetWidth = Math.Max(16, GetSystemMetrics(SmCxSmIcon));
        var targetHeight = Math.Max(16, GetSystemMetrics(SmCySmIcon));

        var bestOffset = 0;
        var bestSize = 0;
        var bestScore = int.MaxValue;

        for (var i = 0; i < count; i++)
        {
            var entry = icoBytes.AsSpan(6 + i * 16, 16);
            var width = entry[0] == 0 ? 256 : entry[0];
            var height = entry[1] == 0 ? 256 : entry[1];
            var bytesInRes = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(8, 4));
            var imageOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(12, 4));

            if (bytesInRes <= 0 || imageOffset < 0 || imageOffset + bytesInRes > icoBytes.Length)
            {
                continue;
            }

            // Prefer the smallest frame that is at least targetWidth, otherwise the closest frame.
            var diff = width - targetWidth;
            var score = diff >= 0 ? diff : 1000 - diff;
            if (score < bestScore)
            {
                bestScore = score;
                bestOffset = imageOffset;
                bestSize = bytesInRes;
            }
        }

        if (bestSize <= 0)
        {
            return IntPtr.Zero;
        }

        var frame = new byte[bestSize];
        Buffer.BlockCopy(icoBytes, bestOffset, frame, 0, bestSize);
        return CreateIconFromResourceEx(
            frame,
            (uint)frame.Length,
            true,
            IconResourceVersion,
            targetWidth,
            targetHeight,
            0);
    }

    private static string TruncateTip(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= 120 ? text : text[..119] + "…";
    }

    private delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassExW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconDataW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct
    {
        public int X;
        public int Y;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassExW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClassW(string lpClassName, nint hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out PointStruct lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreateIconFromResourceEx(
        byte[] presbits,
        uint dwResSize,
        bool fIcon,
        uint dwVer,
        int cxDesired,
        int cyDesired,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NotifyIconDataW lpData);
}
