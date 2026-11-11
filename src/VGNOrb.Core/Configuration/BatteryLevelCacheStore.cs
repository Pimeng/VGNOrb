using System.Text.Json;
using System.Text.Json.Serialization;
using VGNOrb.Core.Devices;

namespace VGNOrb.Core.Configuration;

public sealed record BatteryLevelCacheEntry(int Level, long SavedAtUnixTimeSeconds);

/// <summary>
/// Persists per-device battery display levels between app restarts, the same
/// role the VGN HUB web driver's "bat_&lt;addr&gt;" localStorage entries play.
/// Kept in memory and flushed atomically; a missing or damaged cache file never
/// prevents startup.
/// </summary>
public sealed class BatteryLevelCacheStore
{
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private Dictionary<string, BatteryLevelCacheEntry> _entries = new(StringComparer.Ordinal);
    private bool _dirty;

    public BatteryLevelCacheStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return;
        }

        try
        {
            await using var stream = new FileStream(
                FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var entries = await JsonSerializer
                .DeserializeAsync(
                    stream,
                    BatteryLevelCacheJsonContext.Default.DictionaryStringBatteryLevelCacheEntry,
                    cancellationToken)
                .ConfigureAwait(false);

            if (entries is not null)
            {
                _entries = new Dictionary<string, BatteryLevelCacheEntry>(
                    entries,
                    StringComparer.Ordinal);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged cache only costs the restart continuity of one reading.
        }
    }

    public bool TryGet(string deviceKey, out BatteryLevelSnapshot? snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);

        if (_entries.TryGetValue(deviceKey, out var entry))
        {
            snapshot = new BatteryLevelSnapshot(
                entry.Level,
                DateTimeOffset.FromUnixTimeSeconds(entry.SavedAtUnixTimeSeconds));
            return true;
        }

        snapshot = null;
        return false;
    }

    public void Set(string deviceKey, BatteryLevelSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);
        ArgumentNullException.ThrowIfNull(snapshot);

        var entry = new BatteryLevelCacheEntry(
            snapshot.Level,
            snapshot.SavedAt.ToUnixTimeSeconds());

        if (_entries.TryGetValue(deviceKey, out var existing) && existing == entry)
        {
            return;
        }

        _entries[deviceKey] = entry;
        _dirty = true;
    }

    public async Task SaveIfDirtyAsync(CancellationToken cancellationToken = default)
    {
        if (!_dirty)
        {
            return;
        }

        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_dirty)
            {
                return;
            }

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
                        .SerializeAsync(
                            stream,
                            _entries,
                            BatteryLevelCacheJsonContext.Default.DictionaryStringBatteryLevelCacheEntry,
                            cancellationToken)
                        .ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporaryPath, FilePath, overwrite: true);
                _dirty = false;
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }
        finally
        {
            _saveGate.Release();
        }
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

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, BatteryLevelCacheEntry>))]
internal sealed partial class BatteryLevelCacheJsonContext : JsonSerializerContext
{
}
