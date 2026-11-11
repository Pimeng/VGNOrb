using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace VGNOrb.Avalonia;

public sealed class App : Application
{
    private AppHost? _host;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _host = new AppHost(desktop);
            desktop.Exit += (_, _) =>
            {
                _host?.Dispose();
                _host = null;
            };
            _ = _host.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
