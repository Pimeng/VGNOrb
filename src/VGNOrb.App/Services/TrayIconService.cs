using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using VGNOrb.App.ViewModels;
using VGNOrb.App.Views;

namespace VGNOrb.App.Services;

/// <summary>
/// Owns only operating-system tray integration. Multi-device state and commands
/// remain in the view model.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly OrbWindow _window;
    private readonly OrbViewModel _viewModel;
    private readonly Action _exit;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _summaryItem;
    private readonly ToolStripMenuItem _devicesMenu;
    private readonly ToolStripMenuItem[] _deviceStatusItems;
    private readonly ToolStripMenuItem _refreshAllItem;
    private readonly ToolStripMenuItem _reconnectAllItem;
    private readonly ToolStripMenuItem _disconnectAllItem;
    private readonly ToolStripMenuItem _toggleWindowItem;
    private readonly ToolStripMenuItem _alwaysOnTopItem;
    private readonly Icon _icon;
    private readonly bool _ownsIcon;
    private readonly List<DeviceTileViewModel> _observedTiles = [];
    private bool _disposed;

    public TrayIconService(OrbWindow window, OrbViewModel viewModel, Action exit)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _exit = exit ?? throw new ArgumentNullException(nameof(exit));

        (_icon, _ownsIcon) = LoadIcon();

        _summaryItem = new ToolStripMenuItem { Enabled = false };
        _devicesMenu = new ToolStripMenuItem("设备 (0/6)") { Enabled = false };
        _deviceStatusItems = Enumerable.Range(0, OrbViewModel.MaxDevices)
            .Select(_ => new ToolStripMenuItem { Enabled = false, Visible = false })
            .ToArray();
        _devicesMenu.DropDownItems.AddRange(_deviceStatusItems);

        _refreshAllItem = new ToolStripMenuItem("刷新全部电量");
        _reconnectAllItem = new ToolStripMenuItem("重新发现并连接");
        _disconnectAllItem = new ToolStripMenuItem("断开全部设备");
        _toggleWindowItem = new ToolStripMenuItem("隐藏悬浮球");
        _alwaysOnTopItem = new ToolStripMenuItem("始终置顶") { CheckOnClick = false };
        var exitItem = new ToolStripMenuItem("退出 VGN Orb");

        _refreshAllItem.Click += (_, _) => _viewModel.RefreshAllCommand.Execute(null);
        _reconnectAllItem.Click += (_, _) => _viewModel.ReconnectAllCommand.Execute(null);
        _disconnectAllItem.Click += (_, _) => _viewModel.DisconnectAllCommand.Execute(null);
        _toggleWindowItem.Click += (_, _) => ToggleWindow();
        _alwaysOnTopItem.Click += (_, _) => _viewModel.AlwaysOnTop = !_viewModel.AlwaysOnTop;
        exitItem.Click += (_, _) => _exit();

        _contextMenu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = true
        };
        _contextMenu.Items.AddRange(
        [
            _summaryItem,
            _devicesMenu,
            new ToolStripSeparator(),
            _refreshAllItem,
            _reconnectAllItem,
            _disconnectAllItem,
            new ToolStripSeparator(),
            _toggleWindowItem,
            _alwaysOnTopItem,
            new ToolStripSeparator(),
            exitItem
        ]);
        _contextMenu.Opening += (_, _) => RefreshState();

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "VGN Orb",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };
        _notifyIcon.DoubleClick += (_, _) => ToggleWindow();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Devices.CollectionChanged += OnDevicesCollectionChanged;
        SyncTileSubscriptions();
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
        UnsubscribeFromTiles();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();

        if (_ownsIcon)
        {
            _icon.Dispose();
        }
    }

    private void ToggleWindow()
    {
        if (_window.IsVisible)
        {
            _window.Hide();
        }
        else
        {
            _window.Show();
        }

        RefreshState();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshState();
    }

    private void OnDevicesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        SyncTileSubscriptions();
        RefreshState();
    }

    private void OnTilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshState();
    }

    private void SyncTileSubscriptions()
    {
        UnsubscribeFromTiles();
        foreach (var tile in _viewModel.Devices)
        {
            tile.PropertyChanged += OnTilePropertyChanged;
            _observedTiles.Add(tile);
        }
    }

    private void UnsubscribeFromTiles()
    {
        foreach (var tile in _observedTiles)
        {
            tile.PropertyChanged -= OnTilePropertyChanged;
        }

        _observedTiles.Clear();
    }

    private void RefreshState()
    {
        if (_disposed)
        {
            return;
        }

        var visible = _window.IsVisible;
        var text = $"VGN Orb · {_viewModel.TraySummary}";
        _notifyIcon.Text = text.Length <= 63 ? text : text[..62] + "…";
        _summaryItem.Text = _viewModel.TraySummary;

        var devices = _viewModel.Devices;
        _devicesMenu.Text = $"设备 ({_viewModel.DeviceCountText})";
        _devicesMenu.Enabled = devices.Count > 0;
        for (var index = 0; index < _deviceStatusItems.Length; index++)
        {
            var item = _deviceStatusItems[index];
            if (index >= devices.Count)
            {
                item.Visible = false;
                continue;
            }

            var tile = devices[index];
            item.Visible = true;
            item.Text = $"{tile.KindLabel} · {tile.DeviceName} · {tile.BatteryText}";
            item.ToolTipText = tile.StatusText;
        }

        _refreshAllItem.Enabled = _viewModel.RefreshAllCommand.CanExecute(null);
        _reconnectAllItem.Enabled = _viewModel.ReconnectAllCommand.CanExecute(null);
        _disconnectAllItem.Enabled = _viewModel.DisconnectAllCommand.CanExecute(null);
        _toggleWindowItem.Text = visible ? "隐藏悬浮球" : "显示悬浮球";
        _alwaysOnTopItem.Checked = _viewModel.AlwaysOnTop;
    }

    private static (Icon Icon, bool OwnsIcon) LoadIcon()
    {
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var extracted = Icon.ExtractAssociatedIcon(executablePath);
            if (extracted is not null)
            {
                return (extracted, true);
            }
        }

        return (SystemIcons.Application, false);
    }
}
