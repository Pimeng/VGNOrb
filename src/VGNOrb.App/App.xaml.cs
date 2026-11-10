using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using VGNOrb.App.Services;
using VGNOrb.App.ViewModels;
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
    private DeviceHudController? _hudController;
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
        _hudController?.Dispose();
        _hudController = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
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
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _hudController = new DeviceHudController(_viewModel);
        _hudController.Start();
        _trayIcon = new TrayIconService(_viewModel, RequestShutdown);

        if (_settings.AutoConnectDevice)
        {
            await _viewModel.ReconnectAllCommand.ExecuteAsync(null);
        }
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
            QueueSettingsSave();
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
            _hudController?.Dispose();
            _hudController = null;
            _trayIcon?.Dispose();
            _trayIcon = null;
            Shutdown();
        }
    }
}
