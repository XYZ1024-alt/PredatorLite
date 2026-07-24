using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json.Nodes;
using PredatorLite.Core.Models;
using PredatorLite.Core.Services;
using PredatorLite.Platform.Windows.Acer;

namespace PredatorLite.FanGuard;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string? pipeName = ReadArgument(args, "--pipe");
        string? parentValue = ReadArgument(args, "--parent");
        if (string.IsNullOrWhiteSpace(pipeName) || !int.TryParse(parentValue, out int parentId))
        {
            return 2;
        }

        using FileAppLogger logger = new();
        await using AcerServiceClient service = new(logger);
        AcerWmiClient wmi = new(logger);

        try
        {
            using Process parent = Process.GetProcessById(parentId);
            await using NamedPipeServerStream pipe = new(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            using CancellationTokenSource connectionTimeout = new(TimeSpan.FromSeconds(8));
            await pipe.WaitForConnectionAsync(connectionTimeout.Token).ConfigureAwait(false);
            using StreamReader reader = new(pipe);
            using StreamWriter writer = new(pipe) { AutoFlush = true };
            await writer.WriteLineAsync("READY").ConfigureAwait(false);

            while (!parent.HasExited)
            {
                using CancellationTokenSource heartbeatTimeout = new(TimeSpan.FromSeconds(5));
                string? message;
                try
                {
                    message = await reader.ReadLineAsync(heartbeatTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    logger.LogError("FanGuard heartbeat timed out; restoring automatic fan control.");
                    await RestoreAutomaticFanAsync(service, wmi).ConfigureAwait(false);
                    return 3;
                }

                if (message is null)
                {
                    await RestoreAutomaticFanAsync(service, wmi).ConfigureAwait(false);
                    return 0;
                }

                if (string.Equals(message, "STOP", StringComparison.Ordinal))
                {
                    await RestoreAutomaticFanAsync(service, wmi).ConfigureAwait(false);
                    return 0;
                }

                if (!string.Equals(message, "PING", StringComparison.Ordinal))
                {
                    logger.LogError("FanGuard rejected an unknown pipe message.");
                }
            }

            await RestoreAutomaticFanAsync(service, wmi).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            logger.LogError("FanGuard failed", exception);
            await RestoreAutomaticFanAsync(service, wmi).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task RestoreAutomaticFanAsync(AcerServiceClient service, AcerWmiClient wmi)
    {
        JsonObject parameters = CreateAutomaticFanParameters();

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                AcerResponse response = await service.SetAsync(AcerProtocol.FanControl, parameters).ConfigureAwait(false);
                if (response.IsSuccess)
                {
                    return;
                }
            }
            catch
            {
            }

            if (await wmi.SetFanModeAsync(FanMode.Auto, 50, 50).ConfigureAwait(false))
            {
                return;
            }

            if (attempt < 2)
            {
                await Task.Delay(300).ConfigureAwait(false);
            }
        }
    }

    internal static JsonObject CreateAutomaticFanParameters() => new()
    {
        ["mode"] = (int)FanMode.Auto,
        ["custom_fan_data"] = new JsonArray
        {
            (JsonNode)new JsonObject
            {
                ["fan_custom_auto"] = 1,
                ["fan_custom_speed"] = 50,
                ["fan_name"] = "CPU"
            },
            (JsonNode)new JsonObject
            {
                ["fan_custom_auto"] = 1,
                ["fan_custom_speed"] = 50,
                ["fan_name"] = "GPU"
            }
        }
    };

    private static string? ReadArgument(string[] args, string name)
    {
        int index = Array.FindIndex(args, argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
