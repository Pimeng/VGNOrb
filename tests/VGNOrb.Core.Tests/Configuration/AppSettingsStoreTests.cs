using VGNOrb.Core.Configuration;

namespace VGNOrb.Core.Tests.Configuration;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsSettingsAsJson()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var store = new AppSettingsStore(path);
            var settings = new AppSettings
            {
                AlwaysOnTop = false,
                AutoConnectDevice = false,
                PreferHardware = false,
                PollIntervalMilliseconds = 7500
            };

            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();

            Assert.True(File.Exists(path));
            Assert.Equal(settings, loaded);
            var json = await File.ReadAllTextAsync(path);
            Assert.Contains(@"""alwaysOnTop"": false", json);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Load_WithDamagedJson_FallsBackToDefaults()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path, "{ this is not json");
            var store = new AppSettingsStore(path);

            var loaded = await store.LoadAsync();

            Assert.Equal(new AppSettings(), loaded);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Normalize_ClampsUnsafeManualValues()
    {
        var settings = new AppSettings
        {
            PollIntervalMilliseconds = 5
        };

        var normalized = settings.Normalize();

        Assert.Equal(1000, normalized.PollIntervalMilliseconds);
        Assert.True(normalized.AlwaysOnTop);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "VGNOrb.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
