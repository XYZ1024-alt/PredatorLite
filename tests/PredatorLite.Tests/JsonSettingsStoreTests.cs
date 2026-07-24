using PredatorLite.Core.Models;
using PredatorLite.Core.Services;

namespace PredatorLite.Tests;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task SaveAndLoadRoundTripsSettingsWithoutTemporaryFile()
    {
        string directory = CreateTestDirectory();
        string path = Path.Combine(directory, "settings.json");
        try
        {
            JsonSettingsStore store = new(path);
            AppSettings expected = new()
            {
                Language = "en-US",
                LastAcMode = OperatingMode.Performance,
                FanMode = FanMode.Custom,
                ChargeLimit80Percent = true,
                PreferredRefreshRate = 165
            };

            await store.SaveAsync(expected);
            AppSettings actual = await store.LoadAsync();

            Assert.Equal("en-US", actual.Language);
            Assert.Equal(OperatingMode.Performance, actual.LastAcMode);
            Assert.Equal(FanMode.Custom, actual.FanMode);
            Assert.True(actual.ChargeLimit80Percent);
            Assert.Equal(165, actual.PreferredRefreshRate);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(OperatingMode.Silent)]
    [InlineData(OperatingMode.Balanced)]
    [InlineData(OperatingMode.Performance)]
    [InlineData(OperatingMode.Turbo)]
    public async Task SchemaOneRoundTripsEveryRememberedModeAsString(OperatingMode mode)
    {
        string directory = CreateTestDirectory();
        string path = Path.Combine(directory, "settings.json");
        try
        {
            JsonSettingsStore store = new(path);
            await store.SaveAsync(new AppSettings { LastAcMode = mode });

            string json = await File.ReadAllTextAsync(path);
            AppSettings actual = await store.LoadAsync();

            Assert.Contains("\"SchemaVersion\": 1", json);
            Assert.Contains($"\"LastAcMode\": \"{mode}\"", json);
            Assert.Equal(mode, actual.LastAcMode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SecondSaveCreatesBackupOfPreviousSettings()
    {
        string directory = CreateTestDirectory();
        string path = Path.Combine(directory, "settings.json");
        try
        {
            JsonSettingsStore store = new(path);
            await store.SaveAsync(new AppSettings { Language = "zh-CN" });
            await store.SaveAsync(new AppSettings { Language = "en-US" });

            Assert.True(File.Exists(path + ".bak"));
            Assert.Contains("zh-CN", await File.ReadAllTextAsync(path + ".bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidJsonIsMovedAsideAndDefaultsAreReturned()
    {
        string directory = CreateTestDirectory();
        string path = Path.Combine(directory, "settings.json");
        try
        {
            await File.WriteAllTextAsync(path, "{not-json");
            JsonSettingsStore store = new(path);

            AppSettings settings = await store.LoadAsync();

            Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
            Assert.False(File.Exists(path));
            Assert.Single(Directory.EnumerateFiles(directory, "settings.json.invalid-*"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task UnknownSchemaFallsBackToDefaults()
    {
        string directory = CreateTestDirectory();
        string path = Path.Combine(directory, "settings.json");
        try
        {
            await File.WriteAllTextAsync(path, "{\"SchemaVersion\":999,\"Language\":\"en-US\"}");
            JsonSettingsStore store = new(path);

            AppSettings settings = await store.LoadAsync();

            Assert.Equal("zh-CN", settings.Language);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PredatorLite.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
