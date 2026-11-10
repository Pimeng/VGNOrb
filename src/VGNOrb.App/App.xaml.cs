using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using VGNOrb.App.Services;
using VGNOrb.App.ViewModels;
using VGNOrb.App.Views;
using VGNOrb.Core.Configuration;
using VGNOrb.Core.Devices;
using VGNOrb.Core.Devices.Mock;
using VGNOrb.Devices.Vgn;

namespace VGNOrb.App;

public partial class App : System.Windows.Application
{
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private AppSettingsStore? _settingsStore;
    private AppSettings _settings = new();
    private OrbViewModel? _viewModel;
    private OrbWindow? _orbWindow;
    private TrayIconService? _trayIcon;
    private bool _isShuttingDown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            await InitializeAsync();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                "VGN Orb 启动失败。\n\n" + ex.Message,
                "VGN Orb",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        if (_orbWindow is not null)
        {
            _orbWindow.AllowClose = true;
            _orbWindow.Close();
            _orbWindow = null;
        }

        base.OnExit(e);
    }

    private async Task InitializeAsync()
    {
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VGNOrb",
            "settings.json");

        _settingsStore = new AppSettingsStore(configPath);
        _settings = (await _settingsStore.LoadAsync()).Normalize();

        var mockDiscovery = new MockDeviceDiscoveryService();
        IDeviceDiscoveryService discovery = _settings.PreferHardware
            ? new AutomaticDeviceDiscoveryService(
                [
                    new VgnHidDeviceDiscoveryService(),
                    new VgnKeyboardDiscoveryService()
                ],
                mockDiscovery)
            : mockDiscovery;

        _viewModel = new OrbViewModel(
            discovery,
            _settings.PollIntervalMilliseconds,
            Dispatcher);
        _viewModel.AlwaysOnTop = _settings.AlwaysOnTop;
        _viewModel.TileSize = _settings.OrbSize;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Devices.CollectionChanged += OnDevicesCollectionChanged;

        _orbWindow = new OrbWindow
        {
            DataContext = _viewModel,
            Width = _settings.OrbSize + 16,
            Height = _settings.OrbSize + 16
        };
        PlaceWindow();
        _orbWindow.PositionChanged += OnOrbPositionChanged;

        if (_settings.ShowOrbOnStartup)
        {
            _orbWindow.Show();
        }

        _trayIcon = new TrayIconService(_orbWindow, _viewModel, RequestShutdown);

        if (_settings.AutoConnectDevice)
        {
            await _viewModel.ReconnectAllCommand.ExecuteAsync(null);
        }
    }

    private void PlaceWindow()
    {
        if (_orbWindow is null)
        {
            return;
        }

        var width = _orbWindow.Width;
        var height = _orbWindow.Height;
        var workArea = SystemParameters.WorkArea;
        var savedLeft = _settings.OrbLeft is { } configuredLeft && double.IsFinite(configuredLeft)
            ? configuredLeft
            : (double?)null;
        var savedTop = _settings.OrbTop is { } configuredTop && double.IsFinite(configuredTop)
            ? configuredTop
            : (double?)null;
        var left = savedLeft ?? workArea.Right - width - 32;
        var top = savedTop ?? workArea.Bottom - height - 32;

        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var target = new Rect(left, top, width, height);
        var visibleArea = Rect.Intersect(target, virtualScreen);

        if (visibleArea.IsEmpty || visibleArea.Width < 40 || visibleArea.Height < 40)
        {
            left = workArea.Right - width - 32;
            top = workArea.Bottom - height - 32;
        }

        _orbWindow.Left = left;
        _orbWindow.Top = top;
    }

    private void OnDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(UpdateWindowLayout);
            return;
        }

        UpdateWindowLayout();
    }

    private void UpdateWindowLayout()
    {
        if (_orbWindow is null || _viewModel is null)
        {
            return;
        }

        var count = Math.Max(1, Math.Min(_viewModel.Devices.Count, OrbViewModel.MaxDevices));
        var columns = count switch
        {
            <= 1 => 1,
            2 => 2,
            _ => 3
        };
        var rows = (int)Math.Ceiling(count / (double)columns);
        var tileSize = _settings.OrbSize;

        _orbWindow.Width = (columns * tileSize) + 16;
        _orbWindow.Height = (rows * tileSize) + 16;
        EnsureWindowOnScreen();

        if (double.IsFinite(_orbWindow.Left) && double.IsFinite(_orbWindow.Top))
        {
            _settings = _settings with
            {
                OrbLeft = _orbWindow.Left,
                OrbTop = _orbWindow.Top
            };
            QueueSettingsSave();
        }
    }

    private void EnsureWindowOnScreen()
    {
        if (_orbWindow is null
            || !double.IsFinite(_orbWindow.Left)
            || !double.IsFinite(_orbWindow.Top))
        {
            return;
        }

        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var width = _orbWindow.Width;
        var height = _orbWindow.Height;
        const double taskbarReserve = 56;
        var usableBottom = virtualScreen.Bottom - taskbarReserve;

        var left = _orbWindow.Left;
        var top = _orbWindow.Top;
        if (left + width > virtualScreen.Right)
        {
            left = virtualScreen.Right - width;
        }
        else if (left < virtualScreen.Left)
        {
            left = virtualScreen.Left;
        }

        if (top + height > usableBottom)
        {
            top = usableBottom - height;
        }
        else if (top < virtualScreen.Top)
        {
            top = virtualScreen.Top;
        }

        _orbWindow.Left = left;
        _orbWindow.Top = top;
    }

    private void OnOrbPositionChanged(object? sender, EventArgs e)
    {
        if (_orbWindow is null)
        {
            return;
        }

        _settings = _settings with
        {
            OrbLeft = _orbWindow.Left,
            OrbTop = _orbWindow.Top
        };
        QueueSettingsSave();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null || _isShuttingDown)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(OrbViewModel.AlwaysOnTop):
                _settings = _settings with { AlwaysOnTop = _viewModel.AlwaysOnTop };
                QueueSettingsSave();
                break;
        }
    }

    private void QueueSettingsSave()
    {
        _ = SaveSettingsSafeAsync();
    }

    private async Task SaveSettingsSafeAsync()
    {
        try
        {
            await SaveSettingsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"VGN Orb settings save failed: {ex}");
        }
    }

    private async Task SaveSettingsAsync()
    {
        var store = _settingsStore;
        if (store is null)
        {
            return;
        }

        var snapshot = _settings;
        if (_orbWindow is not null
            && double.IsFinite(_orbWindow.Left)
            && double.IsFinite(_orbWindow.Top))
        {
            snapshot = snapshot with
            {
                OrbLeft = _orbWindow.Left,
                OrbTop = _orbWindow.Top
            };
        }

        if (_viewModel is not null)
        {
            snapshot = snapshot with
            {
                AlwaysOnTop = _viewModel.AlwaysOnTop
            };
        }

        await _saveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await store.SaveAsync(snapshot).ConfigureAwait(false);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private async void RequestShutdown()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;

        try
        {
            if (_viewModel is not null)
            {
                await _viewModel.DisposeAsync();
            }

            await SaveSettingsAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"VGN Orb shutdown cleanup failed: {ex}");
        }
        finally
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
            if (_orbWindow is not null)
            {
                _orbWindow.AllowClose = true;
                _orbWindow.Close();
                _orbWindow = null;
            }

            Shutdown();
        }
    }
}
