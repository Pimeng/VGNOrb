using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using VGNOrb.Avalonia.ViewModels;
using VGNOrb.Core.Configuration;
using VGNOrb.Core.Devices;
using Path = Avalonia.Controls.Shapes.Path;

namespace VGNOrb.Avalonia.Views;

/// <summary>
/// Windows 11 Fluent Dark styled tray context menu for VGN Orb.
/// </summary>
public sealed class TrayMenuWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;

    private const double ShadowLeft = 10;
    private const double ShadowTop = 8;
    private const double ShadowRight = 10;
    private const double ShadowBottom = 14;
    private const double ScreenEdgeMargin = 8;
    private const double IconColumnWidth = 18;
    private const double MenuItemHeight = 30;

    private static readonly FontFamily FluentFont = new("Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI");
    private static readonly IBrush CardBackground = new SolidColorBrush(Color.Parse("#F52B2B2B"));
    private static readonly IBrush CardBorderBrush = new SolidColorBrush(Color.Parse("#24FFFFFF"));
    private static readonly IBrush DeviceCardBackground = new SolidColorBrush(Color.Parse("#0AFFFFFF"));
    private static readonly IBrush DeviceCardBorderBrush = new SolidColorBrush(Color.Parse("#0EFFFFFF"));
    private static readonly IBrush HoverBackground = new SolidColorBrush(Color.Parse("#12FFFFFF"));
    private static readonly IBrush PressedBackground = new SolidColorBrush(Color.Parse("#0AFFFFFF"));
    private static readonly IBrush SeparatorBrush = new SolidColorBrush(Color.Parse("#14FFFFFF"));
    private static readonly IBrush PrimaryTextBrush = new SolidColorBrush(Color.Parse("#F2FFFFFF"));
    private static readonly IBrush SecondaryTextBrush = new SolidColorBrush(Color.Parse("#8CFFFFFF"));
    private static readonly IBrush SubtleCaptionBrush = new SolidColorBrush(Color.Parse("#6EFFFFFF"));
    private static readonly IBrush DeviceNameBrush = new SolidColorBrush(Color.Parse("#F0FFFFFF"));
    private static readonly IBrush DeviceIconBrush = new SolidColorBrush(Color.Parse("#C4FFFFFF"));
    private static readonly IBrush BatteryNormalBrush = new SolidColorBrush(Color.Parse("#B8FFFFFF"));
    private static readonly IBrush BatteryChargingBrush = new SolidColorBrush(Color.Parse("#6CCB5F"));
    private static readonly IBrush BatteryLowBrush = new SolidColorBrush(Color.Parse("#FCE100"));
    private static readonly IBrush BatteryCriticalBrush = new SolidColorBrush(Color.Parse("#FF99A4"));
    private static readonly IBrush ItemIconBrush = new SolidColorBrush(Color.Parse("#D9FFFFFF"));
    private static readonly IBrush DisabledTextBrush = new SolidColorBrush(Color.Parse("#59FFFFFF"));
    private static readonly IBrush ChevronBrush = new SolidColorBrush(Color.Parse("#8CFFFFFF"));
    private static readonly BoxShadows CardShadows = BoxShadows.Parse("0 8 22 0 #6B000000, 0 2 6 0 #38000000");

    // 16x16 viewBox stroke-based Fluent icon geometries.
    private static readonly Geometry MouseIconGeometry = Geometry.Parse(
        "M8,2 C5.6,2 4,3.6 4,6 L4,10 C4,12.4 5.8,14 8,14 C10.2,14 12,12.4 12,10 L12,6 C12,3.6 10.4,2 8,2 Z M8,4.6 L8,7.2");
    private static readonly Geometry KeyboardIconGeometry = Geometry.Parse(
        "M2,4.5 C2,3.9 2.4,3.5 3,3.5 L13,3.5 C13.6,3.5 14,3.9 14,4.5 L14,11.5 C14,12.1 13.6,12.5 13,12.5 L3,12.5 C2.4,12.5 2,12.1 2,11.5 Z M4.5,6.5 L5.5,6.5 M7.5,6.5 L8.5,6.5 M10.5,6.5 L11.5,6.5 M5,9.5 L11,9.5");
    private static readonly Geometry DeviceIconGeometry = Geometry.Parse(
        "M3,3.5 L13,3.5 C13.6,3.5 14,3.9 14,4.5 L14,11.5 C14,12.1 13.6,12.5 13,12.5 L3,12.5 C2.4,12.5 2,12.1 2,11.5 L2,4.5 C2,3.9 2.4,3.5 3,3.5 Z");
    private static readonly Geometry RefreshIconGeometry = Geometry.Parse(
        "M12.8,8 A4.8,4.8 0 1 1 11.4,4.6 M12,2.4 L12,5.1 L9.3,5.1");
    private static readonly Geometry DiscoverIconGeometry = Geometry.Parse(
        "M7,2.8 A4.2,4.2 0 1 0 7,11.2 A4.2,4.2 0 1 0 7,2.8 Z M10.2,10.2 L13.4,13.4");
    private static readonly Geometry DisconnectIconGeometry = Geometry.Parse(
        "M8,2.5 A5.5,5.5 0 1 0 8,13.5 A5.5,5.5 0 1 0 8,2.5 Z M4.2,4.2 L11.8,11.8");
    private static readonly Geometry CornerTopRightGeometry = Geometry.Parse(
        "M2.5,4 C2.5,3.2 3.2,2.5 4,2.5 L12,2.5 C12.8,2.5 13.5,3.2 13.5,4 L13.5,12 C13.5,12.8 12.8,13.5 12,13.5 L4,13.5 C3.2,13.5 2.5,12.8 2.5,12 Z M8.5,5 L11,5 L11,7.5");
    private static readonly Geometry CornerTopLeftGeometry = Geometry.Parse(
        "M2.5,4 C2.5,3.2 3.2,2.5 4,2.5 L12,2.5 C12.8,2.5 13.5,3.2 13.5,4 L13.5,12 C13.5,12.8 12.8,13.5 12,13.5 L4,13.5 C3.2,13.5 2.5,12.8 2.5,12 Z M7.5,5 L5,5 L5,7.5");
    private static readonly Geometry CornerBottomRightGeometry = Geometry.Parse(
        "M2.5,4 C2.5,3.2 3.2,2.5 4,2.5 L12,2.5 C12.8,2.5 13.5,3.2 13.5,4 L13.5,12 C13.5,12.8 12.8,13.5 12,13.5 L4,13.5 C3.2,13.5 2.5,12.8 2.5,12 Z M8.5,11 L11,11 L11,8.5");
    private static readonly Geometry CornerBottomLeftGeometry = Geometry.Parse(
        "M2.5,4 C2.5,3.2 3.2,2.5 4,2.5 L12,2.5 C12.8,2.5 13.5,3.2 13.5,4 L13.5,12 C13.5,12.8 12.8,13.5 12,13.5 L4,13.5 C3.2,13.5 2.5,12.8 2.5,12 Z M7.5,11 L5,11 L5,8.5");
    private static readonly Geometry CheckmarkGeometry = Geometry.Parse(
        "M3,8.5 L6.4,11.8 L13,4.4");
    private static readonly Geometry ChevronRightGeometry = Geometry.Parse(
        "M6,3.5 L10.5,8 L6,12.5");
    private static readonly Geometry ExitIconGeometry = Geometry.Parse(
        "M6.5,2.5 L4,2.5 C3.2,2.5 2.5,3.2 2.5,4 L2.5,12 C2.5,12.8 3.2,13.5 4,13.5 L6.5,13.5 M6.8,8 L13.5,8 M10.8,5.3 L13.5,8 L10.8,10.7");
    private static readonly Geometry ChargingBoltGeometry = Geometry.Parse(
        "M9.5,1.2 L3.8,8.8 L7.8,8.8 L6.5,14.8 L12.2,7.2 L8.2,7.2 Z");

    private readonly OrbViewModel _viewModel;
    private readonly Action _exit;
    private readonly Border _card;
    private readonly DispatcherTimer _submenuOpenTimer;
    private readonly DispatcherTimer _submenuCloseTimer;

    private Border? _cornerRow;
    private TraySubmenuWindow? _submenu;
    private bool _isClosing;

    public TrayMenuWindow(OrbViewModel viewModel, Action exit)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _exit = exit ?? throw new ArgumentNullException(nameof(exit));

        Title = "VGN Orb Menu";
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        ShowActivated = true;

        _submenuOpenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        _submenuOpenTimer.Tick += (_, _) =>
        {
            _submenuOpenTimer.Stop();
            OpenCornerSubmenu();
        };

        _submenuCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _submenuCloseTimer.Tick += (_, _) =>
        {
            _submenuCloseTimer.Stop();
            if (_submenu is not null && !_submenu.IsPointerOver && (_cornerRow is null || !_cornerRow.IsPointerOver))
            {
                CloseSubmenu();
            }
        };

        _card = BuildCard();
        _card.Opacity = 0;
        _card.Transitions =
        [
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(110),
                Easing = new CubicEaseOut()
            }
        ];

        Content = _card;

        Opened += OnWindowOpened;
        Deactivated += OnWindowDeactivated;
        KeyDown += OnWindowKeyDown;
        Closed += OnWindowClosed;
    }

    public void ShowAt(PixelPoint cursorPhysical)
    {
        _card.Measure(Size.Infinity);
        var desired = _card.DesiredSize;

        var screen = Screens.ScreenFromPoint(cursorPhysical) ?? Screens.Primary;
        var scaling = screen?.Scaling ?? 1.0;
        var workArea = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);

        var cardWidthPx = Math.Max(1, (int)Math.Round((desired.Width - ShadowLeft - ShadowRight) * scaling));
        var cardHeightPx = Math.Max(1, (int)Math.Round((desired.Height - ShadowTop - ShadowBottom) * scaling));
        var marginPx = (int)Math.Round(ScreenEdgeMargin * scaling);
        var gapPx = (int)Math.Round(4 * scaling);

        var cardX = cursorPhysical.X;
        if (cardX + cardWidthPx > workArea.Right - marginPx)
        {
            cardX = cursorPhysical.X - cardWidthPx;
        }

        // Prefer opening above the cursor (standard Windows 11 taskbar tray menu behavior).
        var cardY = cursorPhysical.Y - cardHeightPx - gapPx;
        if (cardY < workArea.Y + marginPx)
        {
            cardY = cursorPhysical.Y + gapPx;
        }

        cardX = Math.Clamp(cardX, workArea.X + marginPx, Math.Max(workArea.X + marginPx, workArea.Right - cardWidthPx - marginPx));
        cardY = Math.Clamp(cardY, workArea.Y + marginPx, Math.Max(workArea.Y + marginPx, workArea.Bottom - cardHeightPx - marginPx));

        var windowX = cardX - (int)Math.Round(ShadowLeft * scaling);
        var windowY = cardY - (int)Math.Round(ShadowTop * scaling);
        Position = new PixelPoint(windowX, windowY);

        Show();
        Activate();
    }

    public void Dismiss()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _submenuOpenTimer.Stop();
        _submenuCloseTimer.Stop();
        CloseSubmenu();
        Close();
    }

    private Border BuildCard()
    {
        var items = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 0
        };

        // Top device status card.
        items.Children.Add(BuildDeviceSection());

        // Device actions.
        items.Children.Add(CreateActionItem(
            "刷新全部电量",
            RefreshIconGeometry,
            _viewModel.RefreshAllCommand.CanExecute(null),
            () => _viewModel.RefreshAllCommand.Execute(null)));

        items.Children.Add(CreateActionItem(
            "重新发现并连接",
            DiscoverIconGeometry,
            _viewModel.ReconnectAllCommand.CanExecute(null),
            () => _viewModel.ReconnectAllCommand.Execute(null)));

        items.Children.Add(CreateActionItem(
            "断开全部设备",
            DisconnectIconGeometry,
            _viewModel.DisconnectAllCommand.CanExecute(null),
            () => _viewModel.DisconnectAllCommand.Execute(null)));

        items.Children.Add(CreateSeparator());

        // HUD settings.
        _cornerRow = CreateSubmenuTriggerItem("HUD 位置", CornerIconFor(_viewModel.HudCorner), CornerLabel(_viewModel.HudCorner));
        items.Children.Add(_cornerRow);

        items.Children.Add(CreateToggleItem(
            "始终置顶",
            _viewModel.AlwaysOnTop,
            () => _viewModel.AlwaysOnTop = !_viewModel.AlwaysOnTop));

        items.Children.Add(CreateToggleItem(
            "垂直同步",
            _viewModel.VerticalSync,
            () => _viewModel.VerticalSync = !_viewModel.VerticalSync));

        items.Children.Add(CreateSeparator());

        // Exit.
        items.Children.Add(CreateActionItem(
            "退出 VGN Orb",
            ExitIconGeometry,
            true,
            _exit));

        return new Border
        {
            MinWidth = 238,
            MaxWidth = 282,
            Margin = new Thickness(ShadowLeft, ShadowTop, ShadowRight, ShadowBottom),
            Padding = new Thickness(5, 5, 5, 5),
            CornerRadius = new CornerRadius(8),
            Background = CardBackground,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            BoxShadow = CardShadows,
            Child = items
        };
    }

    private Border BuildDeviceSection()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 5
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };

        var title = new TextBlock
        {
            Text = _viewModel.Devices.Count == 0 ? "未连接设备" : "已连接设备",
            FontFamily = FluentFont,
            FontSize = 11,
            Foreground = SecondaryTextBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 0);
        header.Children.Add(title);

        if (_viewModel.Devices.Count > 0)
        {
            var count = new TextBlock
            {
                Text = $"{_viewModel.ConnectedDeviceCount}/{_viewModel.Devices.Count}",
                FontFamily = FluentFont,
                FontSize = 11,
                Foreground = SubtleCaptionBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(count, 1);
            header.Children.Add(count);
        }

        panel.Children.Add(header);

        foreach (var device in _viewModel.Devices)
        {
            panel.Children.Add(BuildDeviceStatusRow(device));
        }

        return new Border
        {
            Background = DeviceCardBackground,
            BorderBrush = DeviceCardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(2, 2, 2, 6),
            Padding = new Thickness(8, 7, 8, 7),
            Child = panel
        };
    }

    private static Grid BuildDeviceStatusRow(DeviceTileViewModel device)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{IconColumnWidth},*,Auto")
        };

        var iconGeometry = device.Kind switch
        {
            DeviceKind.Mouse => MouseIconGeometry,
            DeviceKind.Keyboard => KeyboardIconGeometry,
            _ => DeviceIconGeometry
        };

        var icon = CreateVectorIcon(iconGeometry, DeviceIconBrush, 14);
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var nameText = new TextBlock
        {
            Text = FormatDeviceName(device.DeviceName),
            FontFamily = FluentFont,
            FontSize = 12.5,
            FontWeight = FontWeight.Medium,
            Foreground = DeviceNameBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0, 12, 0)
        };
        Grid.SetColumn(nameText, 1);
        grid.Children.Add(nameText);

        var batteryPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (device.IsCharging)
        {
            batteryPanel.Children.Add(CreateFilledIcon(ChargingBoltGeometry, BatteryChargingBrush, 13));
        }

        var batteryBrush = GetBatteryBrush(device);
        batteryPanel.Children.Add(new TextBlock
        {
            Text = device.BatteryText,
            FontFamily = FluentFont,
            FontSize = 12,
            Foreground = batteryBrush,
            VerticalAlignment = VerticalAlignment.Center
        });

        Grid.SetColumn(batteryPanel, 2);
        grid.Children.Add(batteryPanel);

        return grid;
    }

    private static IBrush GetBatteryBrush(DeviceTileViewModel device)
    {
        if (!device.IsConnected)
        {
            return SubtleCaptionBrush;
        }

        if (device.IsCharging)
        {
            return DeviceNameBrush;
        }

        return device.BatteryPercent switch
        {
            <= 10 => BatteryCriticalBrush,
            <= 20 => BatteryLowBrush,
            _ => BatteryNormalBrush
        };
    }

    private Border CreateActionItem(string label, Geometry iconGeometry, bool isEnabled, Action onClick)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{IconColumnWidth},*")
        };

        var icon = CreateVectorIcon(iconGeometry, isEnabled ? ItemIconBrush : DisabledTextBrush, 14);
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var text = new TextBlock
        {
            Text = label,
            FontFamily = FluentFont,
            FontSize = 12.5,
            Foreground = isEnabled ? PrimaryTextBrush : DisabledTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 4, 0)
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        return CreateInteractiveRow(grid, isEnabled, () =>
        {
            Dismiss();
            onClick();
        });
    }

    private Border CreateToggleItem(string label, bool isChecked, Action onToggle)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{IconColumnWidth},*")
        };

        if (isChecked)
        {
            var check = CreateVectorIcon(CheckmarkGeometry, PrimaryTextBrush, 13, strokeThickness: 1.5);
            Grid.SetColumn(check, 0);
            grid.Children.Add(check);
        }

        var text = new TextBlock
        {
            Text = label,
            FontFamily = FluentFont,
            FontSize = 12.5,
            Foreground = PrimaryTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 4, 0)
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        return CreateInteractiveRow(grid, true, () =>
        {
            Dismiss();
            onToggle();
        });
    }

    private Border CreateSubmenuTriggerItem(string label, Geometry iconGeometry, string currentValueText)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{IconColumnWidth},*,Auto,Auto")
        };

        var icon = CreateVectorIcon(iconGeometry, ItemIconBrush, 14);
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var text = new TextBlock
        {
            Text = label,
            FontFamily = FluentFont,
            FontSize = 12.5,
            Foreground = PrimaryTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 12, 0)
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var currentText = new TextBlock
        {
            Text = currentValueText,
            FontFamily = FluentFont,
            FontSize = 11.5,
            Foreground = SecondaryTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        Grid.SetColumn(currentText, 2);
        grid.Children.Add(currentText);

        var chevron = CreateVectorIcon(ChevronRightGeometry, ChevronBrush, 11, strokeThickness: 1.35);
        Grid.SetColumn(chevron, 3);
        grid.Children.Add(chevron);

        var row = new Border
        {
            Height = MenuItemHeight,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(9, 0, 7, 0),
            Margin = new Thickness(0, 1),
            Background = Brushes.Transparent,
            Child = grid
        };

        row.PointerEntered += (_, _) =>
        {
            row.Background = HoverBackground;
            _submenuCloseTimer.Stop();
            if (_submenu is null)
            {
                _submenuOpenTimer.Start();
            }
        };

        row.PointerExited += (_, _) =>
        {
            _submenuOpenTimer.Stop();
            if (_submenu is null)
            {
                row.Background = Brushes.Transparent;
            }
            else
            {
                _submenuCloseTimer.Start();
            }
        };

        row.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            {
                row.Background = PressedBackground;
                _submenuOpenTimer.Stop();
                OpenCornerSubmenu();
                e.Handled = true;
            }
        };

        return row;
    }

    private Border CreateInteractiveRow(Control child, bool isEnabled, Action onClick)
    {
        var row = new Border
        {
            Height = MenuItemHeight,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(9, 0, 9, 0),
            Margin = new Thickness(0, 1),
            Background = Brushes.Transparent,
            Child = child
        };

        if (!isEnabled)
        {
            return row;
        }

        row.PointerEntered += (_, _) =>
        {
            row.Background = HoverBackground;
            _submenuOpenTimer.Stop();
            if (_submenu is not null)
            {
                _submenuCloseTimer.Start();
            }
        };

        row.PointerExited += (_, _) =>
        {
            row.Background = Brushes.Transparent;
        };

        row.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            {
                row.Background = PressedBackground;
                e.Handled = true;
            }
        };

        row.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left && row.IsPointerOver)
            {
                e.Handled = true;
                onClick();
            }
        };

        return row;
    }

    private void OpenCornerSubmenu()
    {
        if (_isClosing || _cornerRow is null || _submenu is not null)
        {
            return;
        }

        var rowTopLeft = _cornerRow.PointToScreen(new Point(0, 0));
        var rowBottomRight = _cornerRow.PointToScreen(new Point(_cornerRow.Bounds.Width, _cornerRow.Bounds.Height));
        var cardLeft = _card.PointToScreen(new Point(0, 0)).X;
        var cardRight = _card.PointToScreen(new Point(_card.Bounds.Width, 0)).X;

        var submenu = new TraySubmenuWindow(
            _viewModel.HudCorner,
            corner =>
            {
                Dismiss();
                _viewModel.HudCorner = corner;
            },
            onPointerEntered: () => _submenuCloseTimer.Stop(),
            onPointerExited: () => _submenuCloseTimer.Start(),
            onDeactivated: ScheduleDismissIfInactive);

        _submenu = submenu;
        submenu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_submenu, submenu))
            {
                _submenu = null;
                if (_cornerRow is not null && !_cornerRow.IsPointerOver)
                {
                    _cornerRow.Background = Brushes.Transparent;
                }
            }
        };

        submenu.ShowBeside(cardLeft, cardRight, rowTopLeft.Y, rowBottomRight.Y);
    }

    private void CloseSubmenu()
    {
        var submenu = _submenu;
        _submenu = null;
        if (_cornerRow is not null && !_cornerRow.IsPointerOver)
        {
            _cornerRow.Background = Brushes.Transparent;
        }

        submenu?.Close();
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        ApplyToolWindowStyle(this);
        if (TryGetPlatformHandle()?.Handle is { } hwnd && hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(hwnd);
        }

        Dispatcher.UIThread.Post(() => _card.Opacity = 1, DispatcherPriority.Render);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        ScheduleDismissIfInactive();
    }

    private void ScheduleDismissIfInactive()
    {
        if (_isClosing)
        {
            return;
        }

        // Wait one dispatcher turn so focus transfers between main menu and
        // submenu do not falsely dismiss the menu.
        Dispatcher.UIThread.Post(() =>
        {
            if (_isClosing)
            {
                return;
            }

            if (!IsActive && (_submenu is null || !_submenu.IsActive))
            {
                Dismiss();
            }
        }, DispatcherPriority.Background);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Dismiss();
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _isClosing = true;
        _submenuOpenTimer.Stop();
        _submenuCloseTimer.Stop();
        CloseSubmenu();
    }

    private static Border CreateSeparator()
    {
        return new Border
        {
            Height = 1,
            Background = SeparatorBrush,
            Margin = new Thickness(4, 4, 4, 4)
        };
    }

    private static Viewbox CreateVectorIcon(Geometry geometry, IBrush stroke, double size, double strokeThickness = 1.3)
    {
        return new Viewbox
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            Child = new Canvas
            {
                Width = 16,
                Height = 16,
                Children =
                {
                    new Path
                    {
                        Data = geometry,
                        Stroke = stroke,
                        StrokeThickness = strokeThickness,
                        StrokeLineCap = PenLineCap.Round,
                        StrokeJoin = PenLineJoin.Round
                    }
                }
            }
        };
    }

    private static Viewbox CreateFilledIcon(Geometry geometry, IBrush fill, double size)
    {
        return new Viewbox
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            Child = new Canvas
            {
                Width = 16,
                Height = 16,
                Children =
                {
                    new Path
                    {
                        Data = geometry,
                        Fill = fill
                    }
                }
            }
        };
    }

    private static string FormatDeviceName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.StartsWith("VGN ", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 4)
        {
            return trimmed[4..].TrimStart();
        }

        return trimmed;
    }

    private static Geometry CornerIconFor(HudCorner corner) => corner switch
    {
        HudCorner.TopLeft => CornerTopLeftGeometry,
        HudCorner.BottomRight => CornerBottomRightGeometry,
        HudCorner.BottomLeft => CornerBottomLeftGeometry,
        _ => CornerTopRightGeometry
    };

    private static string CornerLabel(HudCorner corner) => corner switch
    {
        HudCorner.TopLeft => "左上角",
        HudCorner.BottomRight => "右下角",
        HudCorner.BottomLeft => "左下角",
        _ => "右上角"
    };

    private static void ApplyToolWindowStyle(Window window)
    {
        if (window.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero)
        {
            return;
        }

        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        exStyle |= WsExToolWindow;
        SetWindowLongPtr(hwnd, GwlExStyle, new nint(exStyle));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    private sealed class TraySubmenuWindow : Window
    {
        private readonly Border _card;
        private readonly Action _onDeactivated;

        public TraySubmenuWindow(
            HudCorner currentCorner,
            Action<HudCorner> onSelected,
            Action onPointerEntered,
            Action onPointerExited,
            Action onDeactivated)
        {
            _onDeactivated = onDeactivated;

            Title = "VGN Orb Corner Submenu";
            CanResize = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = Brushes.Transparent;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            ShowActivated = false;

            var items = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 0
            };

            foreach (var corner in new[]
                     {
                         HudCorner.TopRight,
                         HudCorner.TopLeft,
                         HudCorner.BottomRight,
                         HudCorner.BottomLeft
                     })
            {
                var target = corner;
                items.Children.Add(CreateCornerItem(CornerLabel(target), currentCorner == target, () => onSelected(target)));
            }

            _card = new Border
            {
                MinWidth = 136,
                Margin = new Thickness(ShadowLeft, ShadowTop, ShadowRight, ShadowBottom),
                Padding = new Thickness(5, 5, 5, 5),
                CornerRadius = new CornerRadius(8),
                Background = CardBackground,
                BorderBrush = CardBorderBrush,
                BorderThickness = new Thickness(1),
                BoxShadow = CardShadows,
                Child = items,
                Opacity = 0,
                Transitions =
                [
                    new DoubleTransition
                    {
                        Property = OpacityProperty,
                        Duration = TimeSpan.FromMilliseconds(95),
                        Easing = new CubicEaseOut()
                    }
                ]
            };

            Content = _card;

            PointerEntered += (_, _) => onPointerEntered();
            PointerExited += (_, _) => onPointerExited();
            Opened += (_, _) =>
            {
                ApplyToolWindowStyle(this);
                Dispatcher.UIThread.Post(() => _card.Opacity = 1, DispatcherPriority.Render);
            };
            Deactivated += (_, _) => _onDeactivated();
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    _onDeactivated();
                }
            };
        }

        public void ShowBeside(int parentCardLeftPx, int parentCardRightPx, int rowTopPx, int rowBottomPx)
        {
            _card.Measure(Size.Infinity);
            var desired = _card.DesiredSize;

            var anchorPoint = new PixelPoint(parentCardRightPx, rowTopPx);
            var screen = Screens.ScreenFromPoint(anchorPoint) ?? Screens.Primary;
            var scaling = screen?.Scaling ?? 1.0;
            var workArea = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);

            var cardWidthPx = Math.Max(1, (int)Math.Round((desired.Width - ShadowLeft - ShadowRight) * scaling));
            var cardHeightPx = Math.Max(1, (int)Math.Round((desired.Height - ShadowTop - ShadowBottom) * scaling));
            var marginPx = (int)Math.Round(ScreenEdgeMargin * scaling);
            var overlapPx = (int)Math.Round(2 * scaling);

            // Open to the right of the parent card; flip to the left if near the right monitor edge.
            var cardX = parentCardRightPx - overlapPx;
            if (cardX + cardWidthPx > workArea.Right - marginPx)
            {
                cardX = parentCardLeftPx - cardWidthPx + overlapPx;
            }

            var topPaddingPx = (int)Math.Round(5 * scaling);
            var cardY = rowTopPx - topPaddingPx;
            if (cardY + cardHeightPx > workArea.Bottom - marginPx)
            {
                cardY = rowBottomPx - cardHeightPx + topPaddingPx;
            }

            cardX = Math.Clamp(cardX, workArea.X + marginPx, Math.Max(workArea.X + marginPx, workArea.Right - cardWidthPx - marginPx));
            cardY = Math.Clamp(cardY, workArea.Y + marginPx, Math.Max(workArea.Y + marginPx, workArea.Bottom - cardHeightPx - marginPx));

            Position = new PixelPoint(
                cardX - (int)Math.Round(ShadowLeft * scaling),
                cardY - (int)Math.Round(ShadowTop * scaling));

            Show();
        }

        private static Border CreateCornerItem(string label, bool isSelected, Action onClick)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions($"{IconColumnWidth},*")
            };

            if (isSelected)
            {
                var check = CreateVectorIcon(CheckmarkGeometry, PrimaryTextBrush, 13, strokeThickness: 1.5);
                Grid.SetColumn(check, 0);
                grid.Children.Add(check);
            }

            var text = new TextBlock
            {
                Text = label,
                FontFamily = FluentFont,
                FontSize = 12.5,
                Foreground = PrimaryTextBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 8, 0)
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var row = new Border
            {
                Height = MenuItemHeight,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(9, 0, 9, 0),
                Margin = new Thickness(0, 1),
                Background = Brushes.Transparent,
                Child = grid
            };

            row.PointerEntered += (_, _) => row.Background = HoverBackground;
            row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
            row.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
                {
                    row.Background = PressedBackground;
                    e.Handled = true;
                }
            };
            row.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left && row.IsPointerOver)
                {
                    e.Handled = true;
                    onClick();
                }
            };

            return row;
        }
    }
}
