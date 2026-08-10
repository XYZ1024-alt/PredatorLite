using System.Management;
using PredatorLite.Platform.Windows.SystemIntegration;

namespace PredatorLite.Tests;

public sealed class WmiOperationOptionsTests
{
    [Fact]
    public void SearchAndInvokeOperationsUseBoundedTimeouts()
    {
        using ManagementObjectSearcher searcher = new("SELECT * FROM Win32_OperatingSystem");

        WmiOperationOptions.Configure(searcher);
        InvokeMethodOptions invokeOptions = WmiOperationOptions.CreateInvokeMethodOptions();

        Assert.Equal(TimeSpan.FromSeconds(2), searcher.Options.Timeout);
        Assert.True(searcher.Options.ReturnImmediately);
        Assert.False(searcher.Options.Rewindable);
        Assert.Equal(TimeSpan.FromSeconds(2), invokeOptions.Timeout);
    }
}
