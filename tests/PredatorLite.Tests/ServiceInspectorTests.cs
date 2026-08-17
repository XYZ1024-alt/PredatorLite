using System.Linq;
using PredatorLite.Core.Models;
using PredatorLite.Platform.Windows.SystemIntegration;

namespace PredatorLite.Tests;

public sealed class ServiceInspectorTests
{
    [Fact]
    public void ReadIncludesAcerAgentServiceAsRequired()
    {
        IReadOnlyList<ManagedServiceInfo> services = ServiceInspector.Read();

        Assert.NotEmpty(services);
        ManagedServiceInfo? agent = services.FirstOrDefault(service =>
            string.Equals(service.Name, "AASSvc", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(agent);
        Assert.True(agent.IsRequired);
        Assert.False(agent.IsManagedConflict);
    }

    [Fact]
    public void RunningServicesExposeFileVersionAndPath()
    {
        IReadOnlyList<ManagedServiceInfo> services = ServiceInspector.Read();

        ManagedServiceInfo? agent = services.FirstOrDefault(service =>
            string.Equals(service.Name, "AASSvc", StringComparison.OrdinalIgnoreCase));
        if (agent is null || agent.Status != "Running")
        {
            return; // Environment without the Acer Agent Service cannot verify this.
        }

        Assert.False(string.IsNullOrWhiteSpace(agent.PathName));
        Assert.False(string.IsNullOrWhiteSpace(agent.FileVersion));
    }

    [Fact]
    public void ReadIncludesLegacyPredatorServiceNameAsConflict()
    {
        IReadOnlyList<ManagedServiceInfo> services = ServiceInspector.Read();

        Assert.NotEmpty(services);
        ManagedServiceInfo? legacy = services.FirstOrDefault(service =>
            string.Equals(service.Name, "PredatorService", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(legacy);
        Assert.True(legacy.IsManagedConflict);
        Assert.False(legacy.IsRequired);
    }

    [Fact]
    public void ReadListsAllRequiredBackends()
    {
        IReadOnlyList<ManagedServiceInfo> services = ServiceInspector.Read();

        string[] required = services
            .Where(service => service.IsRequired)
            .Select(service => service.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(
            ["AASSvc", "AcerLightingService", "AcerQAAgentSvis", "AcerServiceSvc", "ASMSvc"],
            required);
    }
}
