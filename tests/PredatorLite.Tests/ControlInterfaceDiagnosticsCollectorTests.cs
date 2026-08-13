using PredatorLite.Platform.Windows.SystemIntegration;

namespace PredatorLite.Tests;

public sealed class ControlInterfaceDiagnosticsCollectorTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.12.34.56")]
    [InlineData("::1")]
    public void LoopbackAddressesAreRecognized(string address) =>
        Assert.True(ControlInterfaceDiagnosticsCollector.IsLoopback(address));

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public void WildcardListenerAddressesAreRecognized(string address) =>
        Assert.True(ControlInterfaceDiagnosticsCollector.IsWildcard(address));

    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("8.8.8.8")]
    [InlineData("fe80::1")]
    [InlineData("")]
    public void NonLoopbackAddressesAreRejected(string address)
    {
        Assert.False(ControlInterfaceDiagnosticsCollector.IsLoopback(address));
        Assert.False(ControlInterfaceDiagnosticsCollector.IsWildcard(address));
    }

    [Theory]
    [InlineData("127.0.0.1", "0.0.0.0")]
    [InlineData("0.0.0.0", null)]
    [InlineData("192.168.1.10", "::1")]
    public void DiagnosticEndpointRequiresLoopbackOrWildcard(
        string localAddress,
        string? remoteAddress) =>
        Assert.True(ControlInterfaceDiagnosticsCollector.IsDiagnosticEndpoint(
            localAddress,
            remoteAddress));

    [Theory]
    [InlineData("192.168.1.10", "8.8.8.8")]
    [InlineData("10.0.0.4", "1.1.1.1")]
    public void ExternalEndpointsAreExcluded(string localAddress, string remoteAddress) =>
        Assert.False(ControlInterfaceDiagnosticsCollector.IsDiagnosticEndpoint(
            localAddress,
            remoteAddress));

    [Theory]
    [InlineData(42, 46933, null)]
    [InlineData(42, 60000, 46753)]
    [InlineData(42, 5141, 60000)]
    public void OwnedKnownControlEndpointsAreIncluded(
        int processId,
        int localPort,
        int? remotePort) =>
        Assert.True(ControlInterfaceDiagnosticsCollector.IsKnownControlEndpoint(
            processId,
            localPort,
            remotePort));

    [Theory]
    [InlineData(0, 46933, null)]
    [InlineData(0, 60000, 46753)]
    [InlineData(42, 60000, 60001)]
    public void UnownedOrUnrelatedControlEndpointsAreExcluded(
        int processId,
        int localPort,
        int? remotePort) =>
        Assert.False(ControlInterfaceDiagnosticsCollector.IsKnownControlEndpoint(
            processId,
            localPort,
            remotePort));

    [Theory]
    [InlineData(2, "Listen")]
    [InlineData(5, "Established")]
    [InlineData(11, "TimeWait")]
    [InlineData(100, "Bound")]
    public void TcpStatesUseReadableNames(int state, string expected) =>
        Assert.Equal(expected, ControlInterfaceDiagnosticsCollector.ToTcpState(state));

    [Theory]
    [InlineData(
        "\"C:\\Program Files\\Acer\\PredatorSense\\PredatorSense.exe\" --background",
        "C:\\Program Files\\Acer\\PredatorSense\\PredatorSense.exe")]
    [InlineData(
        "C:\\Program Files\\Acer\\PredatorSense\\PredatorSense.exe --background",
        "C:\\Program Files\\Acer\\PredatorSense\\PredatorSense.exe")]
    [InlineData(@"C:\Acer\AcerService.exe", @"C:\Acer\AcerService.exe")]
    public void ExecutablePathIsExtractedWithoutArguments(string commandLine, string expected) =>
        Assert.Equal(expected, ControlInterfaceDiagnosticsCollector.ExtractExecutablePath(commandLine));

    [Theory]
    [InlineData("")]
    [InlineData("not-an-executable --argument")]
    [InlineData("\"unterminated")]
    public void InvalidExecutablePathsAreRejected(string commandLine) =>
        Assert.Null(ControlInterfaceDiagnosticsCollector.ExtractExecutablePath(commandLine));
}
