using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json.Nodes;
using PredatorLite.Core.Models;
using PredatorLite.Core.Services;
using PredatorLite.Platform.Windows;
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
        if (!HardwareTargetProfileCatalog.TryResolveCurrent(out HardwareTargetProfile? profile) ||
            profile is null ||
            !profile.AuthorizedControls.HasFlag(HardwareControlCapabilities.FanControl))
        {
            logger.LogError("FanGuard found no authorized hardware profile for fan recovery.");
            return 4;
        }

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
                    FanRecoveryStatus recovery = await RestoreAutomaticFanAsync(service, wmi)
                        .ConfigureAwait(false);
                    LogRecoveryStatus(logger, recovery);
                    return RecoveryExitCode(recovery, 3);
                }

                if (message is null)
                {
                    FanRecoveryStatus recovery = await RestoreAutomaticFanAsync(service, wmi)
                        .ConfigureAwait(false);
                    LogRecoveryStatus(logger, recovery);
                    return RecoveryExitCode(recovery, 0);
                }

                if (string.Equals(message, "STOP", StringComparison.Ordinal))
                {
                    FanRecoveryStatus recovery = await RestoreAutomaticFanAsync(service, wmi)
                        .ConfigureAwait(false);
                    LogRecoveryStatus(logger, recovery);
                    return RecoveryExitCode(recovery, 0);
                }

                if (!string.Equals(message, "PING", StringComparison.Ordinal))
                {
                    logger.LogError("FanGuard rejected an unknown pipe message.");
                }
            }

            FanRecoveryStatus parentExitRecovery = await RestoreAutomaticFanAsync(service, wmi)
                .ConfigureAwait(false);
            LogRecoveryStatus(logger, parentExitRecovery);
            return RecoveryExitCode(parentExitRecovery, 0);
        }
        catch (Exception exception)
        {
            logger.LogError("FanGuard failed", exception);
            FanRecoveryStatus failureRecovery = await RestoreAutomaticFanAsync(service, wmi)
                .ConfigureAwait(false);
            LogRecoveryStatus(logger, failureRecovery);
            return 1;
        }
    }

    internal enum FanRecoveryStatus
    {
        Failed,
        Verified,
        TransportAccepted
    }

    private static void LogRecoveryStatus(FileAppLogger logger, FanRecoveryStatus status)
    {
        if (status == FanRecoveryStatus.Failed)
        {
            logger.LogError("FanGuard could not restore automatic fan control.");
        }
        else if (status == FanRecoveryStatus.TransportAccepted)
        {
            logger.LogError("FanGuard WMI recovery was accepted without a fan-mode read-back endpoint.");
        }
    }

    internal static int RecoveryExitCode(FanRecoveryStatus status, int verifiedExitCode) =>
        status switch
        {
            FanRecoveryStatus.Verified => verifiedExitCode,
            FanRecoveryStatus.TransportAccepted => 2,
            _ => 1
        };

    private static async Task<FanRecoveryStatus> RestoreAutomaticFanAsync(
        AcerServiceClient service,
        AcerWmiClient wmi)
    {
        JsonObject parameters = CreateAutomaticFanParameters();

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                AcerResponse response = await service.SetAsync(
                    AcerProtocol.FanControl,
                    parameters).ConfigureAwait(false);
                if (response.IsSuccess && await IsAutomaticFanVerifiedAsync(service).ConfigureAwait(false))
                {
                    return FanRecoveryStatus.Verified;
                }
            }
            catch
            {
            }

            if (await wmi.SetFanModeAsync(FanMode.Auto, 50, 50).ConfigureAwait(false))
            {
                return FanRecoveryStatus.TransportAccepted;
            }

            if (attempt < 2)
            {
                await Task.Delay(300).ConfigureAwait(false);
            }
        }

        return FanRecoveryStatus.Failed;
    }

    private static async Task<bool> IsAutomaticFanVerifiedAsync(AcerServiceClient service)
    {
        AcerResponse response = await service.QueryAsync(AcerProtocol.FanControl).ConfigureAwait(false);
        return response.IsSuccess &&
            response.TryGetInt("mode", out int mode) &&
            mode == (int)FanMode.Auto;
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
