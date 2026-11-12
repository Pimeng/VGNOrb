using System.Text.Json;
using System.Text.Json.Serialization;

namespace VGNOrb.Core.Configuration;

/// <summary>
/// Small atomic JSON settings store. It is intentionally not generic: keeping the
/// settings type concrete keeps the persistence boundary easy to read and test.
/// </summary>
public sealed class AppSettingsStore
{
    public AppSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = await File
                .ReadAllTextAsync(FilePath, cancellationToken)
                .ConfigureAwait(false);

            using var document = JsonDocument.Parse(json);
            var settings = JsonSerializer.Deserialize(
                document.RootElement,
                AppSettingsJsonContext.Default.AppSettings) ?? new AppSettings();

            return RestoreMissingDefaults(document.RootElement, settings).Normalize();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged or inaccessible settings file should never prevent startup.
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = FilePath + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer
                    .SerializeAsync(stream, settings.Normalize(), AppSettingsJsonContext.Default.AppSettings, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    /// <summary>
    /// The source-generated deserializer assigns <c>default(T)</c> to members that
    /// are absent from the file instead of keeping their declared initializer, so
    /// a settings file written by an older build silently turns every default-true
    /// flag off. Restore the declared defaults for keys the file does not contain.
    /// </summary>
    private static AppSettings RestoreMissingDefaults(JsonElement root, AppSettings settings)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new AppSettings();
        }

        var defaults = new AppSettings();
        return settings with
        {
            AlwaysOnTop = ReadBoolean(root, "alwaysOnTop") ?? defaults.AlwaysOnTop,
            AutoConnectDevice = ReadBoolean(root, "autoConnectDevice") ?? defaults.AutoConnectDevice,
            PreferHardware = ReadBoolean(root, "preferHardware") ?? defaults.PreferHardware,
            VerticalSync = ReadBoolean(root, "verticalSync") ?? defaults.VerticalSync,
            PollIntervalMilliseconds = ReadInt32(root, "pollIntervalMilliseconds")
                ?? defaults.PollIntervalMilliseconds,
            HudCorner = root.TryGetProperty("hudCorner", out _) ? settings.HudCorner : defaults.HudCorner
        };
    }

    private static bool? ReadBoolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static int? ReadInt32(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
            ? number
            : null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup only.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup only.
        }
    }
}
