using System.Management;

namespace PredatorLite.Platform.Windows.SystemIntegration;

internal static class WmiOperationOptions
{
    internal static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(2);

    public static void Configure(ManagementObjectSearcher searcher)
    {
        searcher.Options.Timeout = OperationTimeout;
        searcher.Options.ReturnImmediately = true;
        searcher.Options.Rewindable = false;
    }

    public static InvokeMethodOptions CreateInvokeMethodOptions() =>
        new(null, OperationTimeout);
}
