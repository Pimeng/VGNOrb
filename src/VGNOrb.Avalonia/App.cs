using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

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
            if (Program.HasParentConsole)
            {
                Console.CancelKeyPress += OnCancelKeyPress;
            }

            _ = _host.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Ctrl+C in the launching terminal runs the same cleanup as the tray's
    // exit item instead of killing the process mid-save.
    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        Dispatcher.UIThread.Post(() => _host?.RequestShutdown());
    }
}
