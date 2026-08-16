using System.Text.Json.Nodes;
using PredatorLite.Core.Abstractions;
using PredatorLite.Platform.Windows.Acer;

namespace PredatorLite.Tests;

public sealed class AcerServiceClientTests
{
    private sealed class NullLogger : IAppLogger
    {
        public string LogDirectory => "unused";
        public void Info(string message) { }
        public void LogError(string message, Exception? exception = null) { }
        public void Dispose() { }
    }

    private sealed class ScriptedServiceClient(
        IAppLogger logger,
        byte[]? aesKey,
        IReadOnlyList<Func<byte[]?, AcerResponse>> responses)
        : AcerServiceClient(logger, aesKey)
    {
        private int _call;
        private readonly IReadOnlyList<Func<byte[]?, AcerResponse>> _responses = responses;

        internal override Task<AcerResponse> SendCoreAsync(
            uint packetId,
            string function,
            JsonObject? parameters,
            byte[]? aesKey,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_responses[_call++](aesKey));
        }
    }

    private static AcerResponse Response(int result, string request = "OPERATING_MODE") =>
        new(result, request, null, $"{{\"result\" : \"{result}\"}}");

    private static readonly byte[] Key = System.Text.Encoding.ASCII.GetBytes("A6052DC8A6E44210");

    [Fact]
    public async Task AesRejectionFallsBackToPlaintextAndCachesIt()
    {
        // First call with AES -> rejected (result=3). Plaintext retry -> success (result=0).
        bool[] transportSeen = [false, false];
        var client = new ScriptedServiceClient(
            new NullLogger(),
            Key,
            [
                key =>
                {
                    Assert.NotNull(key);
                    transportSeen[0] = true;
                    return Response(3);
                },
                key =>
                {
                    Assert.Null(key);
                    transportSeen[1] = true;
                    return Response(0);
                }
            ]);

        AcerResponse first = await client.QueryAsync(AcerProtocol.OperatingMode);
        Assert.True(first.IsSuccess);
        Assert.True(transportSeen[0] && transportSeen[1]);
    }

    [Fact]
    public async Task PlaintextRetryOnlyHappensOnRejection()
    {
        // AES accepted on first try -> no fallback, no plaintext attempt.
        bool plaintextSeen = false;
        var client = new ScriptedServiceClient(
            new NullLogger(),
            Key,
            [
                key =>
                {
                    Assert.NotNull(key);
                    return Response(0);
                },
                key =>
                {
                    plaintextSeen = true;
                    return Response(0);
                }
            ]);

        AcerResponse first = await client.QueryAsync(AcerProtocol.OperatingMode);
        Assert.True(first.IsSuccess);
        Assert.False(plaintextSeen);
    }

    [Fact]
    public async Task PlaintextFallbackIsCachedForSubsequentRequests()
    {
        // First request: AES rejected, plaintext succeeds -> caches plaintext.
        // Second request: must go plaintext directly (no AES attempt).
        int aesAttempts = 0;
        bool secondRequestPlaintext = false;
        var client = new ScriptedServiceClient(
            new NullLogger(),
            Key,
            [
                key => { aesAttempts++; return Response(3); },
                key =>
                {
                    Assert.Null(key);
                    return Response(0);
                },
                key =>
                {
                    Assert.Null(key);
                    secondRequestPlaintext = true;
                    return Response(0);
                }
            ]);

        AcerResponse first = await client.QueryAsync(AcerProtocol.OperatingMode);
        Assert.True(first.IsSuccess);

        AcerResponse second = await client.QueryAsync(AcerProtocol.OperatingMode);
        Assert.True(second.IsSuccess);
        Assert.True(secondRequestPlaintext);
        Assert.Equal(1, aesAttempts);
    }

    [Fact]
    public async Task PlaintextRetryThatAlsoFailsKeepsConfiguredTransport()
    {
        // AES rejected and plaintext also rejected -> keep AES mode; subsequent requests retry AES first.
        int aesAttempts = 0;
        var client = new ScriptedServiceClient(
            new NullLogger(),
            Key,
            [
                key => { aesAttempts++; return Response(3); },
                key => Response(3),
                key => { aesAttempts++; return Response(0); }
            ]);

        AcerResponse first = await client.QueryAsync(AcerProtocol.OperatingMode);
        Assert.False(first.IsSuccess);

        AcerResponse second = await client.QueryAsync(AcerProtocol.OperatingMode);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, aesAttempts);
    }

    [Fact]
    public async Task NoAesKeySendsPlaintextWithoutFallbackLogic()
    {
        bool plaintextSeen = false;
        var client = new ScriptedServiceClient(
            new NullLogger(),
            null,
            [
                key =>
                {
                    Assert.Null(key);
                    plaintextSeen = true;
                    return Response(0);
                }
            ]);

        AcerResponse response = await client.QueryAsync(AcerProtocol.OperatingMode);
        Assert.True(response.IsSuccess);
        Assert.True(plaintextSeen);
    }
}
