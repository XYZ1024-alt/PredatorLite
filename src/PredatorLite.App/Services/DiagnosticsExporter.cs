using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using PredatorLite.Core.Models;

namespace PredatorLite.App.Services;

public sealed class DiagnosticsExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task ExportAsync(
        string destinationPath,
        DeviceCapabilities capabilities,
        HardwareSnapshot snapshot,
        IReadOnlyDictionary<DeviceSettingId, DeviceSettingState> deviceSettings,
        IReadOnlyList<ManagedServiceInfo> services,
        AppSettings settings,
        string logDirectory,
        CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using FileStream output = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: false);
        await WriteJsonAsync(archive, "device.json", capabilities, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(archive, "snapshot.json", snapshot, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(archive, "device-settings.json", deviceSettings, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(archive, "services.json", services, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(archive, "settings.json", settings, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            archive,
            "application.json",
            new
            {
                Version = typeof(DiagnosticsExporter).Assembly.GetName().Version?.ToString(),
                Runtime = Environment.Version.ToString(),
                Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                ExportedUtc = DateTimeOffset.UtcNow
            },
            cancellationToken).ConfigureAwait(false);

        if (!Directory.Exists(logDirectory))
        {
            return;
        }

        foreach (string logPath in Directory.EnumerateFiles(logDirectory, "PredatorLite-*.log")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(3))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ZipArchiveEntry entry = archive.CreateEntry($"logs/{Path.GetFileName(logPath)}", CompressionLevel.Optimal);
            await using Stream target = entry.Open();
            await using FileStream source = File.Open(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteJsonAsync<T>(
        ZipArchive archive,
        string name,
        T value,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using Stream stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
    }
}
