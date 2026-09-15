using Liviate.Rag.Exceptions;
using Liviate.Rag.Internal;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Liviate.Rag.Tests;

/// <summary>Focused tests for VectorStoreClient's token-exchange behavior: concurrent
/// cache-miss dedup and defensive parsing of the exchange response.</summary>
public class VectorStoreClientTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    public void Dispose() => _server.Stop();

    [Fact]
    public async Task ConcurrentSearchesShareOneTokenExchange()
    {
        _server
            .Given(Request.Create().WithPath("/api/tenancy/vectordb/exchange-token/").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                token = "t",
                access = "r",
                expires_at = 9999999999,
                collection_name = "ns__docs",
                qdrant_url = _server.Url!,
            }));
        _server
            .Given(Request.Create().WithPath("/collections/ns__docs/points/query").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { result = new { points = Array.Empty<object>() } }));

        using var client = new VectorStoreClient("test-key", TimeSpan.FromSeconds(10), $"{_server.Url}/api/tenancy/vectordb/exchange-token/");

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.SearchAsync("docs", new[] { 0.1f, 0.2f }, topK: 3, filter: null)));

        var exchangeCalls = _server.LogEntries.Count(e => e.RequestMessage?.Path == "/api/tenancy/vectordb/exchange-token/");
        Assert.Equal(1, exchangeCalls);
    }

    [Fact]
    public async Task MissingExchangeFieldThrowsWithoutLeakingFieldName()
    {
        _server
            .Given(Request.Create().WithPath("/api/tenancy/vectordb/exchange-token/").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                token = "t",
                access = "r",
                expires_at = 9999999999,
                collection_name = "ns__docs",
                // "qdrant_url" deliberately missing
            }));

        using var client = new VectorStoreClient("test-key", TimeSpan.FromSeconds(10), $"{_server.Url}/api/tenancy/vectordb/exchange-token/");

        var exc = await Assert.ThrowsAsync<LiviateException>(() => client.SearchAsync("docs", new[] { 0.1f, 0.2f }, topK: 3, filter: null));
        Assert.DoesNotContain("qdrant", exc.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-plane URL", exc.Message);
    }
}
