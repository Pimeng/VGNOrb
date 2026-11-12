using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using VGNOrb.Avalonia.ViewModels;
using VGNOrb.Core.Configuration;

namespace VGNOrb.Avalonia.Services;

public sealed class TrayIconService : IDisposable
{
    private const string IconUri = "avares://VGNOrb/Assets/orb.png";
    private readonly OrbViewModel _viewModel;
    private readonly Action _exit;
    private readonly TrayIcon _trayIcon;
    private readonly TrayIcons _trayIcons;
    private readonly List<DeviceTileViewModel> _observedDevices = [];
    private bool _disposed;

    public TrayIconService(OrbViewModel viewModel, Action exit)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _exit = exit ?? throw new ArgumentNullException(nameof(exit));

        using var iconStream = AssetLoader.Open(new Uri(IconUri));
        var iconBitmap = new Bitmap(iconStream);
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconBitmap),
            ToolTipText = "VGN Orb"
        };
        _trayIcons = new TrayIcons { _trayIcon };
        if (Application.Current is { } application)
        {
            TrayIcon.SetIcons(application, _trayIcons);
        }
        _trayIcon.IsVisible = true;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Devices.CollectionChanged += OnDevicesCollectionChanged;
        SyncDeviceSubscriptions();
        RefreshState();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Devices.CollectionChanged -= OnDevicesCollectionChanged;
        UnsubscribeFromDevices();
        _trayIcon.IsVisible = false;
        _trayIcons.Remove(_trayIcon);
        _trayIcon.Dispose();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshState();
    }

    private void OnDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncDeviceSubscriptions();
        RefreshState();
    }

    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshState();
    }

    private void SyncDeviceSubscriptions()
    {
        UnsubscribeFromDevices();
        foreach (var device in _viewModel.Devices)
        {
            device.PropertyChanged += OnDevicePropertyChanged;
            _observedDevices.Add(device);
        }
    }

    private void UnsubscribeFromDevices()
    {
        foreach (var device in _observedDevices)
        {
            device.PropertyChanged -= OnDevicePropertyChanged;
        }

        _observedDevices.Clear();
    }

    private void RefreshState()
    {
        if (_disposed)
        {
            return;
        }

        _trayIcon.ToolTipText = Truncate($"VGN Orb · {_viewModel.TraySummary}", 120);
        _trayIcon.Menu = BuildMenu();
    }

    private NativeMenu BuildMenu()
    {
        var menu = new NativeMenu();
        menu.Add(new NativeMenuItem
        {
            Header = _viewModel.TraySummary,
            IsEnabled = false
        });

        foreach (var device in _viewModel.Devices)
        {
            menu.Add(new NativeMenuItem
            {
                Header = $"{device.KindLabel} · {device.DeviceName} · {device.BatteryText}",
                IsEnabled = false
            });
        }

        menu.Add(new NativeMenuItemSeparator());
        menu.Add(new NativeMenuItem
        {
            Header = "刷新全部电量",
            Command = _viewModel.RefreshAllCommand,
            IsEnabled = _viewModel.RefreshAllCommand.CanExecute(null)
        });
        menu.Add(new NativeMenuItem
        {
            Header = "重新发现并连接",
            Command = _viewModel.ReconnectAllCommand,
            IsEnabled = _viewModel.ReconnectAllCommand.CanExecute(null)
        });
        menu.Add(new NativeMenuItem
        {
            Header = "断开全部设备",
            Command = _viewModel.DisconnectAllCommand,
            IsEnabled = _viewModel.DisconnectAllCommand.CanExecute(null)
        });
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(BuildCornerMenu());
        var alwaysOnTop = new NativeMenuItem
        {
            Header = _viewModel.AlwaysOnTop ? "始终置顶 ✓" : "始终置顶"
        };
        alwaysOnTop.Click += (_, _) => _viewModel.AlwaysOnTop = !_viewModel.AlwaysOnTop;
        menu.Add(alwaysOnTop);
        var verticalSync = new NativeMenuItem
        {
            Header = _viewModel.VerticalSync ? "垂直同步 ✓" : "垂直同步"
        };
        verticalSync.Click += (_, _) => _viewModel.VerticalSync = !_viewModel.VerticalSync;
        menu.Add(verticalSync);
        menu.Add(new NativeMenuItemSeparator());
        var exit = new NativeMenuItem { Header = "退出 VGN Orb" };
        exit.Click += (_, _) => _exit();
        menu.Add(exit);

        return menu;
    }

    private NativeMenuItem BuildCornerMenu()
    {
        var positionMenu = new NativeMenu();
        foreach (var corner in new[]
                 {
                     HudCorner.TopRight,
                     HudCorner.TopLeft,
                     HudCorner.BottomRight,
                     HudCorner.BottomLeft
                 })
        {
            var target = corner;
            var label = CornerLabel(target);
            var item = new NativeMenuItem
            {
                Header = _viewModel.HudCorner == target ? $"✓ {label}" : label
            };
            item.Click += (_, _) => _viewModel.HudCorner = target;
            positionMenu.Add(item);
        }

        return new NativeMenuItem
        {
            Header = "HUD 位置",
            Menu = positionMenu
        };
    }

    private static string CornerLabel(HudCorner corner) => corner switch
    {
        HudCorner.TopLeft => "左上角",
        HudCorner.BottomRight => "右下角",
        HudCorner.BottomLeft => "左下角",
        _ => "右上角"
    };

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
    }
}

