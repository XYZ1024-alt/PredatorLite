using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PredatorLite.Core.Abstractions;
using PredatorLite.Core.Models;

namespace PredatorLite.Core.Services;

public sealed class GitHubApplicationUpdateService : IApplicationUpdateService
{
    private const int MaxMetadataBytes = 1024 * 1024;
    private const int MaxChecksumBytes = 1024;
    private const long MaxInstallerBytes = 256L * 1024 * 1024;
    private const string GitHubApiVersion = "2026-03-10";

    private readonly HttpClient _httpClient;
    private readonly Uri _latestReleaseUri;
    private readonly string _repositoryOwner;
    private readonly string _repositoryName;
    private readonly string _repositoryDownloadPrefix;
    private readonly string _repositoryReleasePrefix;
    private readonly string _downloadDirectory;
    private readonly string _userAgent;
    private bool _disposed;

    public GitHubApplicationUpdateService(
        Uri latestReleaseUri,
        string repositoryOwner,
        string repositoryName,
        string downloadDirectory,
        string userAgentVersion)
        : this(
            new HttpClientHandler(),
            latestReleaseUri,
            repositoryOwner,
            repositoryName,
            downloadDirectory,
            userAgentVersion)
    {
    }

    public GitHubApplicationUpdateService(
        HttpMessageHandler httpMessageHandler,
        Uri latestReleaseUri,
        string repositoryOwner,
        string repositoryName,
        string downloadDirectory,
        string userAgentVersion)
    {
        ArgumentNullException.ThrowIfNull(httpMessageHandler);
        ArgumentNullException.ThrowIfNull(latestReleaseUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryOwner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgentVersion);

        _repositoryOwner = repositoryOwner;
        _repositoryName = repositoryName;
        _repositoryDownloadPrefix = $"/{repositoryOwner}/{repositoryName}/releases/download/";
        _repositoryReleasePrefix = $"/{repositoryOwner}/{repositoryName}/releases/tag/";
        _latestReleaseUri = latestReleaseUri;
        ValidateLatestReleaseUri(_latestReleaseUri);
        _downloadDirectory = Path.GetFullPath(downloadDirectory);
        _userAgent = $"PredatorLite/{userAgentVersion}";
        _httpClient = new HttpClient(httpMessageHandler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<ApplicationUpdateCheckResult> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(currentVersion);

        using HttpRequestMessage request = CreateRequest(_latestReleaseUri, "application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", GitHubApiVersion);
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        ValidateLatestReleaseUri(response.RequestMessage?.RequestUri ?? _latestReleaseUri);

        byte[] payload = await ReadLimitedContentAsync(
            response.Content,
            MaxMetadataBytes,
            cancellationToken).ConfigureAwait(false);
        GitHubReleaseResponse release = JsonSerializer.Deserialize(
                payload,
                PredatorLiteJsonContext.Default.GitHubReleaseResponse) ??
            throw new InvalidDataException("GitHub returned an empty release response.");
        if (release.Draft || release.Prerelease)
        {
            throw new InvalidDataException("The latest-release endpoint returned a non-stable release.");
        }

        Version latestVersion = ParseStableVersion(release.TagName);
        if (latestVersion <= currentVersion)
        {
            return new ApplicationUpdateCheckResult(latestVersion, null);
        }

        string versionText = latestVersion.ToString(3);
        Uri releasePageUri = ParseReleasePageUri(release.HtmlUrl, latestVersion);
        string installerFileName = $"PredatorLite-Setup-{versionText}-win-x64.exe";
        string checksumFileName = $"{installerFileName}.sha256";
        GitHubReleaseAssetResponse installerAsset = GetSingleAsset(release.Assets, installerFileName);
        GitHubReleaseAssetResponse checksumAsset = GetSingleAsset(release.Assets, checksumFileName);
        if (!string.Equals(installerAsset.State, "uploaded", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(checksumAsset.State, "uploaded", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The stable release assets are not fully uploaded.");
        }
        if (installerAsset.Size <= 0 || installerAsset.Size > MaxInstallerBytes)
        {
            throw new InvalidDataException("The stable installer size is outside the accepted range.");
        }

        Uri installerUri = ParseAssetUri(installerAsset.BrowserDownloadUrl, installerFileName);
        Uri checksumUri = ParseAssetUri(checksumAsset.BrowserDownloadUrl, checksumFileName);
        string? installerDigest = ParseOptionalDigest(installerAsset.Digest);
        return new ApplicationUpdateCheckResult(
            latestVersion,
            new ApplicationUpdate(
                latestVersion,
                releasePageUri,
                installerFileName,
                installerUri,
                checksumUri,
                installerAsset.Size,
                installerDigest));
    }

    public async Task<string> DownloadInstallerAsync(
        ApplicationUpdate update,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(update);
        ValidateUpdate(update);

        byte[] expectedHash = await DownloadExpectedHashAsync(update, cancellationToken)
            .ConfigureAwait(false);
        if (update.InstallerDigest is not null)
        {
            byte[] apiHash = Convert.FromHexString(update.InstallerDigest);
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, apiHash))
            {
                throw new InvalidDataException("The installer checksum does not match GitHub's asset digest.");
            }
        }

        Directory.CreateDirectory(_downloadDirectory);
        string installerPath = Path.Combine(_downloadDirectory, update.InstallerFileName);
        string partialPath = $"{installerPath}.{Guid.NewGuid():N}.download";
        progress?.Report(0);
        try
        {
            using HttpRequestMessage request = CreateRequest(update.InstallerDownloadUri, "application/octet-stream");
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            ValidateAssetResponseUri(response.RequestMessage?.RequestUri ?? update.InstallerDownloadUri);
            if (response.Content.Headers.ContentLength is long contentLength &&
                contentLength != update.InstallerSize)
            {
                throw new InvalidDataException("The installer content length does not match release metadata.");
            }

            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using FileStream destination = new(
                partialPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });
            using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
            long totalBytes = 0;
            try
            {
                while (true)
                {
                    int bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    totalBytes += bytesRead;
                    if (totalBytes > update.InstallerSize || totalBytes > MaxInstallerBytes)
                    {
                        throw new InvalidDataException("The installer download exceeded the declared size.");
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                        .ConfigureAwait(false);
                    hasher.AppendData(buffer, 0, bytesRead);
                    progress?.Report((int)Math.Min(99, totalBytes * 100 / update.InstallerSize));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (totalBytes != update.InstallerSize)
            {
                throw new InvalidDataException("The installer download was incomplete.");
            }

            byte[] actualHash = hasher.GetHashAndReset();
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
            {
                throw new InvalidDataException("The downloaded installer failed SHA-256 verification.");
            }

            await destination.DisposeAsync().ConfigureAwait(false);
            File.Move(partialPath, installerPath, overwrite: true);
            progress?.Report(100);
            return installerPath;
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private HttpRequestMessage CreateRequest(Uri uri, string accept)
    {
        HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.UserAgent.ParseAdd(_userAgent);
        return request;
    }

    private async Task<byte[]> DownloadExpectedHashAsync(
        ApplicationUpdate update,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(update.ChecksumDownloadUri, "application/octet-stream");
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        ValidateAssetResponseUri(response.RequestMessage?.RequestUri ?? update.ChecksumDownloadUri);
        byte[] payload = await ReadLimitedContentAsync(
            response.Content,
            MaxChecksumBytes,
            cancellationToken).ConfigureAwait(false);
        string checksumLine = Encoding.ASCII.GetString(payload).Trim();
        if (checksumLine.Length <= 66 ||
            checksumLine[64] != ' ' ||
            checksumLine[65] != ' ' ||
            !IsSha256Hex(checksumLine.AsSpan(0, 64)) ||
            !string.Equals(checksumLine[66..], update.InstallerFileName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The installer checksum sidecar is malformed.");
        }

        return Convert.FromHexString(checksumLine.AsSpan(0, 64));
    }

    private void ValidateUpdate(ApplicationUpdate update)
    {
        string expectedInstallerName = $"PredatorLite-Setup-{update.Version.ToString(3)}-win-x64.exe";
        if (!string.Equals(update.InstallerFileName, expectedInstallerName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The update installer name does not match its version.");
        }
        if (update.InstallerSize <= 0 || update.InstallerSize > MaxInstallerBytes)
        {
            throw new InvalidDataException("The update installer size is outside the accepted range.");
        }

        ValidateReleasePageUri(update.ReleasePageUri, update.Version);
        ValidateAssetUri(update.InstallerDownloadUri, update.InstallerFileName);
        ValidateAssetUri(update.ChecksumDownloadUri, $"{update.InstallerFileName}.sha256");
        if (update.InstallerDigest is not null && !IsSha256Hex(update.InstallerDigest))
        {
            throw new InvalidDataException("The update installer digest is malformed.");
        }
    }

    private static GitHubReleaseAssetResponse GetSingleAsset(
        IReadOnlyList<GitHubReleaseAssetResponse>? assets,
        string expectedName)
    {
        GitHubReleaseAssetResponse[] matches = assets?
            .Where(asset => string.Equals(asset.Name, expectedName, StringComparison.Ordinal))
            .ToArray() ?? [];
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidDataException($"The stable release must contain exactly one {expectedName} asset.");
    }

    private Uri ParseAssetUri(string? value, string expectedName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            throw new InvalidDataException($"The {expectedName} download URL is invalid.");
        }

        ValidateAssetUri(uri, expectedName);
        return uri;
    }

    private Uri ParseReleasePageUri(string? value, Version expectedVersion)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            throw new InvalidDataException("The stable release page URL is invalid.");
        }

        ValidateReleasePageUri(uri, expectedVersion);
        return uri;
    }

    private void ValidateLatestReleaseUri(Uri uri)
    {
        string expectedPath = $"/repos/{_repositoryOwner}/{_repositoryName}/releases/latest";
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidDataException("The latest-release URL is outside the configured GitHub repository.");
        }
    }

    private void ValidateAssetUri(Uri uri, string expectedName)
    {
        string fileName = Uri.UnescapeDataString(uri.Segments[^1]);
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith(_repositoryDownloadPrefix, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(fileName, expectedName, StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidDataException($"The {expectedName} asset URL is outside the configured GitHub repository.");
        }
    }

    private void ValidateReleasePageUri(Uri uri, Version expectedVersion)
    {
        bool validRepositoryPath = uri.AbsolutePath.StartsWith(
            _repositoryReleasePrefix,
            StringComparison.OrdinalIgnoreCase);
        string tagName = validRepositoryPath
            ? Uri.UnescapeDataString(uri.AbsolutePath[_repositoryReleasePrefix.Length..])
            : string.Empty;
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !validRepositoryPath ||
            string.IsNullOrWhiteSpace(tagName) ||
            tagName.Contains('/') ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !ParseStableVersion(tagName).Equals(expectedVersion))
        {
            throw new InvalidDataException(
                "The stable release page URL is outside the configured GitHub repository.");
        }
    }

    private static void ValidateAssetResponseUri(Uri uri)
    {
        bool trustedHost = uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || !trustedHost)
        {
            throw new InvalidDataException("GitHub redirected an update asset to an untrusted location.");
        }
    }

    private static Version ParseStableVersion(string? tagName)
    {
        string normalized = tagName?.Trim() ?? string.Empty;
        if (normalized.StartsWith('v'))
        {
            normalized = normalized[1..];
        }
        if (!Version.TryParse(normalized, out Version? parsed) ||
            parsed.Build < 0 ||
            parsed.Revision >= 0 ||
            normalized.Count(character => character == '.') != 2)
        {
            throw new InvalidDataException("The stable release tag is not a three-component version.");
        }

        return new Version(parsed.Major, parsed.Minor, parsed.Build, 0);
    }

    private static string? ParseOptionalDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return null;
        }
        if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The installer asset digest does not use SHA-256.");
        }

        string hash = digest[7..];
        return IsSha256Hex(hash)
            ? hash
            : throw new InvalidDataException("The installer asset digest is malformed.");
    }

    private static bool IsSha256Hex(string value) => IsSha256Hex(value.AsSpan());

    private static bool IsSha256Hex(ReadOnlySpan<char> value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!((character >= '0' && character <= '9') ||
                (character >= 'a' && character <= 'f') ||
                (character >= 'A' && character <= 'F')))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<byte[]> ReadLimitedContentAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long contentLength && contentLength > maxBytes)
        {
            throw new InvalidDataException("The update response exceeded the accepted size.");
        }

        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using MemoryStream buffer = new();
        byte[] chunk = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (true)
            {
                int bytesRead = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    return buffer.ToArray();
                }
                if (buffer.Length + bytesRead > maxBytes)
                {
                    throw new InvalidDataException("The update response exceeded the accepted size.");
                }

                buffer.Write(chunk, 0, bytesRead);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}

internal sealed class GitHubReleaseResponse
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; init; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }

    [JsonPropertyName("draft")]
    public bool Draft { get; init; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; init; }

    [JsonPropertyName("assets")]
    public IReadOnlyList<GitHubReleaseAssetResponse>? Assets { get; init; }
}

internal sealed class GitHubReleaseAssetResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("digest")]
    public string? Digest { get; init; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; init; }
}
