using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using Avalonia;
using Avalonia.Platform;
using VGNOrb.Avalonia.ViewModels;
using VGNOrb.Avalonia.Views;

namespace VGNOrb.Avalonia.Services;

public sealed class TrayIconService : IDisposable
{
    private const string IconUri = "avares://VGNOrb/Assets/orb.ico";

    private readonly OrbViewModel _viewModel;
    private readonly Action _exit;
    private readonly Win32TrayIcon _trayIcon;
    private readonly List<DeviceTileViewModel> _observedDevices = [];
    private TrayMenuWindow? _activeMenu;
    private bool _disposed;

    public TrayIconService(OrbViewModel viewModel, Action exit)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _exit = exit ?? throw new ArgumentNullException(nameof(exit));

        var iconBytes = LoadIconBytes();
        _trayIcon = new Win32TrayIcon(iconBytes, "VGN Orb");
        _trayIcon.ContextMenuRequested += OnContextMenuRequested;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Devices.CollectionChanged += OnDevicesCollectionChanged;
        SyncDeviceSubscriptions();
        RefreshTooltip();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trayIcon.ContextMenuRequested -= OnContextMenuRequested;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Devices.CollectionChanged -= OnDevicesCollectionChanged;
        UnsubscribeFromDevices();

        _activeMenu?.Dismiss();
        _activeMenu = null;
        _trayIcon.Dispose();
    }

    private void OnContextMenuRequested(PixelPoint cursorPhysical)
    {
        if (_disposed)
        {
            return;
        }

        _activeMenu?.Dismiss();

        var menu = new TrayMenuWindow(_viewModel, _exit);
        _activeMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeMenu, menu))
            {
                _activeMenu = null;
            }
        };

        menu.ShowAt(cursorPhysical);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshTooltip();
    }

    private void OnDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncDeviceSubscriptions();
        RefreshTooltip();
    }

    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshTooltip();
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

    private void RefreshTooltip()
    {
        if (_disposed)
        {
            return;
        }

        _trayIcon.ToolTipText = $"VGN Orb · {_viewModel.TraySummary}";
    }

    private static byte[] LoadIconBytes()
    {
        using var stream = AssetLoader.Open(new Uri(IconUri));
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
