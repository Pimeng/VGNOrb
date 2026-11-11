using System.Runtime.InteropServices;
using Avalonia;

namespace VGNOrb.Avalonia;

internal static class Program
{
    private const uint MessageBoxIconError = 0x00000010;

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            if (TryDescribeMissingNativeDependency(ex, out var message))
            {
                ShowMessageBox(message);
                Environment.ExitCode = 1;
                return;
            }

            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }

    private static bool TryDescribeMissingNativeDependency(Exception exception, out string message)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DllNotFoundException)
            {
                message =
                    "VGN Orb 缺少运行所需的原生组件，无法启动。\n\n" +
                    "请完整解压发布压缩包，不要只复制 VGNOrb.exe。\n" +
                    "以下文件必须与 VGNOrb.exe 位于同一目录：\n\n" +
                    "  libSkiaSharp.dll\n" +
                    "  libHarfBuzzSharp.dll\n" +
                    "  av_libglesv2.dll\n\n" +
                    "详细信息已写入：\n" +
                    "%APPDATA%\\VGNOrb\\startup-error.log";
                return true;
            }
        }

        message = string.Empty;
        return false;
    }

    private static void ShowMessageBox(string message)
    {
        try
        {
            MessageBoxW(IntPtr.Zero, message, "VGN Orb", MessageBoxIconError);
        }
        catch
        {
            // The startup log still contains the original exception.
        }
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>
    /// Framework initialization failures happen before AppHost can show its own
    /// error window. Persist the full exception, including the inner exception
    /// that the native runtime does not put in the event log.
    /// </summary>
    private static void WriteStartupError(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VGNOrb");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "startup-error.log"),
                $"[{DateTimeOffset.Now:O}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never mask the original failure.
        }
    }
}
