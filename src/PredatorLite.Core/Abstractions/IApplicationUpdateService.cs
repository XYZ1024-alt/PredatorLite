using PredatorLite.Core.Models;

namespace PredatorLite.Core.Abstractions;

public interface IApplicationUpdateService : IDisposable
{
    Task<ApplicationUpdateCheckResult> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default);

    Task<string> DownloadInstallerAsync(
        ApplicationUpdate update,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}
