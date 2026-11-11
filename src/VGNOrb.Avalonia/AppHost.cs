using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VGNOrb.Avalonia.Services;
using VGNOrb.Avalonia.ViewModels;
using VGNOrb.Core.Configuration;
using VGNOrb.Core.Devices;
using VGNOrb.Core.Devices.Mock;
using VGNOrb.Devices.Vgn;

namespace VGNOrb.Avalonia;

public sealed class AppHost : IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private AppSettingsStore? _settingsStore;
    private AppSettings _settings = new();
    private OrbViewModel? _viewModel;
    private DeviceHudController? _hudController;
    private TrayIconService? _trayIcon;
    private bool _isShuttingDown;
    private bool _disposed;

    public AppHost(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
    }

    public async Task StartAsync()
    {
        try
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
                Dispatcher.UIThread);
            _viewModel.AlwaysOnTop = _settings.AlwaysOnTop;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            _hudController = new DeviceHudController(_viewModel);
            _hudController.Start();
            _trayIcon = new TrayIconService(_viewModel, RequestShutdown);

            if (_settings.AutoConnectDevice)
            {
                await _viewModel.ReconnectAllCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"VGN Orb startup failed: {ex}");
            ShowStartupError(ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hudController?.Dispose();
        _hudController = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null || _isShuttingDown)
        {
            return;
        }

        if (e.PropertyName == nameof(OrbViewModel.AlwaysOnTop))
        {
            _settings = _settings with { AlwaysOnTop = _viewModel.AlwaysOnTop };
            _ = SaveSettingsSafeAsync();
        }
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
        if (_viewModel is not null)
        {
            snapshot = snapshot with { AlwaysOnTop = _viewModel.AlwaysOnTop };
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
            Dispose();
            _desktop.Shutdown();
        }
    }

    private void ShowStartupError(Exception exception)
    {
        var window = new Window
        {
            Title = "VGN Orb",
            Width = 520,
            Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new TextBlock
            {
                Text = "VGN Orb 启动失败。\n\n" + exception.Message,
                Margin = new global::Avalonia.Thickness(20),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        window.Closed += (_, _) => _desktop.Shutdown(1);
        window.Show();
    }
}
