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
                ShowOrbOnStartup = false,
                AutoConnectDevice = false,
                PreferHardware = false,
                OrbSize = 144,
                OrbLeft = 321.5,
                OrbTop = 123.25,
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
            OrbSize = 1,
            PollIntervalMilliseconds = 5,
            OrbLeft = double.NaN,
            OrbTop = 100
        };

        var normalized = settings.Normalize();

        Assert.Equal(72, normalized.OrbSize);
        Assert.Equal(1000, normalized.PollIntervalMilliseconds);
        Assert.Null(normalized.OrbLeft);
        Assert.Equal(100, normalized.OrbTop);
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
