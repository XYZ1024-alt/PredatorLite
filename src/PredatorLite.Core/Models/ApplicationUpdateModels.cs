namespace PredatorLite.Core.Models;

public sealed record ApplicationUpdateCheckResult(
    Version LatestVersion,
    ApplicationUpdate? Update)
{
    public bool IsUpdateAvailable => Update is not null;
}

public sealed record ApplicationUpdate(
    Version Version,
    string InstallerFileName,
    Uri InstallerDownloadUri,
    Uri ChecksumDownloadUri,
    long InstallerSize,
    string? InstallerDigest);
