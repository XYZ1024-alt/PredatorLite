using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PredatorLite.Core.Models;
using PredatorLite.Core.Services;

namespace PredatorLite.Tests;

public sealed class GitHubApplicationUpdateServiceTests
{
    private static readonly Uri LatestReleaseUri = new(
        "https://api.github.com/repos/XYZ1024-alt/PredatorLite/releases/latest",
        UriKind.Absolute);

    [Fact]
    public async Task NewStableReleaseSelectsInstallerAndChecksumAssets()
    {
        string directory = CreateTempDirectory();
        try
        {
            byte[] installer = Encoding.UTF8.GetBytes("installer-payload");
            string hash = Convert.ToHexString(SHA256.HashData(installer));
            using GitHubApplicationUpdateService service = CreateService(
                new StubHttpMessageHandler(request =>
                {
                    Assert.Equal(LatestReleaseUri, request.RequestUri);
                    Assert.Contains("PredatorLite/1.0.2", request.Headers.UserAgent.ToString());
                    Assert.Contains("2026-03-10", request.Headers.GetValues("X-GitHub-Api-Version"));
                    return CreateReleaseResponse("v1.1.0", installer.Length, hash);
                }),
                directory);

            ApplicationUpdateCheckResult result = await service.CheckAsync(new Version(1, 0, 2, 0));

            ApplicationUpdate update = Assert.IsType<ApplicationUpdate>(result.Update);
            Assert.True(result.IsUpdateAvailable);
            Assert.Equal(new Version(1, 1, 0, 0), result.LatestVersion);
            Assert.Equal("PredatorLite-Setup-1.1.0-win-x64.exe", update.InstallerFileName);
            Assert.Equal(installer.Length, update.InstallerSize);
            Assert.Equal(hash, update.InstallerDigest);
            Assert.Equal(
                "https://github.com/XYZ1024-alt/PredatorLite/releases/tag/v1.1.0",
                update.ReleasePageUri.AbsoluteUri);
            Assert.Equal(
                "https://github.com/XYZ1024-alt/PredatorLite/releases/download/v1.1.0/PredatorLite-Setup-1.1.0-win-x64.exe",
                update.InstallerDownloadUri.AbsoluteUri);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OlderStableReleaseIsReportedAsUpToDateWithoutAssets()
    {
        string directory = CreateTempDirectory();
        try
        {
            using GitHubApplicationUpdateService service = CreateService(
                new StubHttpMessageHandler(_ => CreateReleaseResponse(
                    "v1.0.1",
                    installerSize: 0,
                    installerHash: null,
                    includeAssets: false)),
                directory);

            ApplicationUpdateCheckResult result = await service.CheckAsync(new Version(1, 0, 2, 0));

            Assert.False(result.IsUpdateAvailable);
            Assert.Null(result.Update);
            Assert.Equal(new Version(1, 0, 1, 0), result.LatestVersion);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PrereleaseResponseIsRejected()
    {
        string directory = CreateTempDirectory();
        try
        {
            using GitHubApplicationUpdateService service = CreateService(
                new StubHttpMessageHandler(_ => CreateReleaseResponse(
                    "v1.1.0",
                    installerSize: 1,
                    installerHash: new string('0', 64),
                    prerelease: true)),
                directory);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => service.CheckAsync(new Version(1, 0, 2, 0)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task VerifiedInstallerIsWrittenOnlyAfterHashAndSizeMatch()
    {
        string directory = CreateTempDirectory();
        try
        {
            byte[] installer = Encoding.UTF8.GetBytes("verified-installer-payload");
            string hash = Convert.ToHexString(SHA256.HashData(installer));
            string installerName = "PredatorLite-Setup-1.1.0-win-x64.exe";
            Uri checksumUri = new(
                $"https://github.com/XYZ1024-alt/PredatorLite/releases/download/v1.1.0/{installerName}.sha256");
            Uri installerUri = new(
                $"https://github.com/XYZ1024-alt/PredatorLite/releases/download/v1.1.0/{installerName}");
            using GitHubApplicationUpdateService service = CreateService(
                new StubHttpMessageHandler(request =>
                {
                    if (request.RequestUri == LatestReleaseUri)
                    {
                        return CreateReleaseResponse("v1.1.0", installer.Length, hash);
                    }
                    if (request.RequestUri == checksumUri)
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent($"{hash}  {installerName}\r\n", Encoding.ASCII)
                        };
                    }
                    if (request.RequestUri == installerUri)
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new ByteArrayContent(installer)
                        };
                    }

                    throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
                }),
                directory);
            ApplicationUpdateCheckResult check = await service.CheckAsync(new Version(1, 0, 2, 0));
            List<int> progressValues = [];
            Progress<int> progress = new(progressValues.Add);

            string path = await service.DownloadInstallerAsync(check.Update!, progress);

            Assert.Equal(Path.Combine(directory, installerName), path);
            Assert.Equal(installer, await File.ReadAllBytesAsync(path));
            Assert.Contains(0, progressValues);
            Assert.Contains(100, progressValues);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.download"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task HashMismatchRejectsInstallerAndDeletesPartialFile()
    {
        string directory = CreateTempDirectory();
        try
        {
            byte[] installer = Encoding.UTF8.GetBytes("tampered-installer-payload");
            byte[] expectedInstaller = Encoding.UTF8.GetBytes("expected-installer-payload");
            string expectedHash = Convert.ToHexString(SHA256.HashData(expectedInstaller));
            string installerName = "PredatorLite-Setup-1.1.0-win-x64.exe";
            Uri checksumUri = new(
                $"https://github.com/XYZ1024-alt/PredatorLite/releases/download/v1.1.0/{installerName}.sha256");
            Uri installerUri = new(
                $"https://github.com/XYZ1024-alt/PredatorLite/releases/download/v1.1.0/{installerName}");
            ApplicationUpdate update = new(
                new Version(1, 1, 0, 0),
                new Uri("https://github.com/XYZ1024-alt/PredatorLite/releases/tag/v1.1.0"),
                installerName,
                installerUri,
                checksumUri,
                installer.Length,
                InstallerDigest: null);
            using GitHubApplicationUpdateService service = CreateService(
                new StubHttpMessageHandler(request =>
                {
                    if (request.RequestUri == checksumUri)
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                $"{expectedHash}  {installerName}\r\n",
                                Encoding.ASCII)
                        };
                    }
                    if (request.RequestUri == installerUri)
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new ByteArrayContent(installer)
                        };
                    }

                    throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
                }),
                directory);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => service.DownloadInstallerAsync(update));

            Assert.False(File.Exists(Path.Combine(directory, installerName)));
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AssetOutsideConfiguredRepositoryIsRejected()
    {
        string directory = CreateTempDirectory();
        try
        {
            byte[] installer = Encoding.UTF8.GetBytes("installer-payload");
            string hash = Convert.ToHexString(SHA256.HashData(installer));
            HttpResponseMessage response = CreateReleaseResponse("v1.1.0", installer.Length, hash);
            string json = await response.Content.ReadAsStringAsync();
            response.Dispose();
            json = json.Replace(
                "https://github.com/XYZ1024-alt/PredatorLite/releases/download/",
                "https://example.com/XYZ1024-alt/PredatorLite/releases/download/",
                StringComparison.Ordinal);
            using GitHubApplicationUpdateService service = CreateService(
                new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                }),
                directory);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => service.CheckAsync(new Version(1, 0, 2, 0)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReleasePageOutsideConfiguredRepositoryIsRejected()
    {
        string directory = CreateTempDirectory();
        try
        {
            using GitHubApplicationUpdateService service = CreateService(
                new StubHttpMessageHandler(_ => CreateReleaseResponse(
                    "v1.1.0",
                    installerSize: 1,
                    installerHash: new string('0', 64),
                    releasePageUrl: "https://example.com/XYZ1024-alt/PredatorLite/releases/tag/v1.1.0")),
                directory);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => service.CheckAsync(new Version(1, 0, 2, 0)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static GitHubApplicationUpdateService CreateService(
        HttpMessageHandler handler,
        string downloadDirectory) => new(
            handler,
            LatestReleaseUri,
            "XYZ1024-alt",
            "PredatorLite",
            downloadDirectory,
            "1.0.2");

    private static HttpResponseMessage CreateReleaseResponse(
        string tagName,
        long installerSize,
        string? installerHash,
        bool includeAssets = true,
        bool prerelease = false,
        string? releasePageUrl = null)
    {
        string version = tagName.TrimStart('v');
        string installerName = $"PredatorLite-Setup-{version}-win-x64.exe";
        object[] assets = includeAssets
            ?
            [
                new
                {
                    name = installerName,
                    state = "uploaded",
                    size = installerSize,
                    digest = installerHash is null ? null : $"sha256:{installerHash}",
                    browser_download_url =
                        $"https://github.com/XYZ1024-alt/PredatorLite/releases/download/{tagName}/{installerName}"
                },
                new
                {
                    name = $"{installerName}.sha256",
                    state = "uploaded",
                    size = 104,
                    digest = (string?)null,
                    browser_download_url =
                        $"https://github.com/XYZ1024-alt/PredatorLite/releases/download/{tagName}/{installerName}.sha256"
                }
            ]
            : [];
        string json = JsonSerializer.Serialize(new
        {
            tag_name = tagName,
            html_url = releasePageUrl ??
                $"https://github.com/XYZ1024-alt/PredatorLite/releases/tag/{tagName}",
            draft = false,
            prerelease,
            assets
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PredatorLite.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HttpResponseMessage response = responseFactory(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }
}
