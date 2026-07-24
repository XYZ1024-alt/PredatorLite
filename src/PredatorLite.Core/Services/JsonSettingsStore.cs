using System.Text.Json;
using PredatorLite.Core.Abstractions;
using PredatorLite.Core.Models;

namespace PredatorLite.Core.Services;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonSettingsStore(string? settingsPath = null)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        SettingsPath = settingsPath ?? Path.Combine(appData, "PredatorLite", "settings.json");
    }

    public string SettingsPath { get; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            await using FileStream stream = File.OpenRead(SettingsPath);
            AppSettings? settings = await JsonSerializer.DeserializeAsync(
                stream,
                PredatorLiteJsonContext.Default.AppSettings,
                cancellationToken).ConfigureAwait(false);

            return settings is { SchemaVersion: AppSettings.CurrentSchemaVersion }
                ? settings
                : new AppSettings();
        }
        catch (JsonException)
        {
            TryBackUpInvalidSettings();
            return new AppSettings();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string directory = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(directory);
            string temporaryPath = SettingsPath + ".tmp";

            await using (FileStream stream = new(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    PredatorLiteJsonContext.Default.AppSettings,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(SettingsPath))
            {
                File.Copy(SettingsPath, SettingsPath + ".bak", true);
            }

            File.Move(temporaryPath, SettingsPath, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private void TryBackUpInvalidSettings()
    {
        try
        {
            string invalidPath = SettingsPath + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Move(SettingsPath, invalidPath, true);
        }
        catch
        {
        }
    }
}
