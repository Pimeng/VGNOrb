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
                PollIntervalMilliseconds = 7500,
                HudCorner = HudCorner.BottomLeft
            };

            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();

            Assert.True(File.Exists(path));
            Assert.Equal(settings, loaded);
            var json = await File.ReadAllTextAsync(path);
            Assert.Contains(@"""alwaysOnTop"": false", json);
            Assert.Contains(@"""hudCorner"": ""BottomLeft""", json);
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
    public async Task Load_WithoutHudCorner_UsesTopRightDefault()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path, """{ "alwaysOnTop": false }""");
            var store = new AppSettingsStore(path);

            var loaded = await store.LoadAsync();

            Assert.Equal(HudCorner.TopRight, loaded.HudCorner);
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
            PollIntervalMilliseconds = 5,
            HudCorner = (HudCorner)99
        };

        var normalized = settings.Normalize();

        Assert.Equal(1000, normalized.PollIntervalMilliseconds);
        Assert.Equal(HudCorner.TopRight, normalized.HudCorner);
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
