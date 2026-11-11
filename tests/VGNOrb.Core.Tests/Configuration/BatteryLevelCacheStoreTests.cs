using VGNOrb.Core.Configuration;
using VGNOrb.Core.Devices;

namespace VGNOrb.Core.Tests.Configuration;

public sealed class BatteryLevelCacheStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "VGNOrb.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoad_RoundTripsPerDeviceSnapshots()
    {
        var path = Path.Combine(_directory, "battery-levels.json");
        var savedAt = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var store = new BatteryLevelCacheStore(path);
        store.Set("vgn-f1se:abc", new BatteryLevelSnapshot(57, savedAt));
        await store.SaveIfDirtyAsync();

        var reloaded = new BatteryLevelCacheStore(path);
        await reloaded.LoadAsync();

        Assert.True(reloaded.TryGet("vgn-f1se:abc", out var snapshot));
        Assert.NotNull(snapshot);
        Assert.Equal(57, snapshot.Level);
        Assert.Equal(savedAt, snapshot.SavedAt);
    }

    [Fact]
    public async Task TryGet_UnknownDevice_ReturnsFalse()
    {
        var store = new BatteryLevelCacheStore(Path.Combine(_directory, "missing.json"));
        await store.LoadAsync();

        Assert.False(store.TryGet("nope", out var snapshot));
        Assert.Null(snapshot);
    }

    [Fact]
    public async Task SaveIfDirty_WithoutChanges_DoesNotWriteFile()
    {
        var path = Path.Combine(_directory, "battery-levels.json");
        var store = new BatteryLevelCacheStore(path);

        await store.SaveIfDirtyAsync();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task LoadAsync_DamagedFile_StartsEmpty()
    {
        var path = Path.Combine(_directory, "battery-levels.json");
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(path, "{ not json");

        var store = new BatteryLevelCacheStore(path);
        await store.LoadAsync();

        Assert.False(store.TryGet("anything", out _));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temp cleanup is best-effort.
        }
    }
}
