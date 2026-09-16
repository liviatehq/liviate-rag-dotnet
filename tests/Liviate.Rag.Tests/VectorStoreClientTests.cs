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

    [Fact]
    public async Task ExchangeRequestSendsCollectionField()
    {
        // The exchange endpoint's current field name is "collection" (the older "name" is kept
        // as a permanent server-side alias, but this client should send the current name).
        _server
            .Given(Request.Create().WithPath("/api/tenancy/vectordb/exchange-token/").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                token = "t", access = "r", expires_at = 9999999999,
                collection_name = "ns__docs", qdrant_url = _server.Url!,
            }));
        _server
            .Given(Request.Create().WithPath("/collections/ns__docs/points/query").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { result = new { points = Array.Empty<object>() } }));

        using var client = new VectorStoreClient("test-key", TimeSpan.FromSeconds(10), $"{_server.Url}/api/tenancy/vectordb/exchange-token/");
        await client.SearchAsync("docs", new[] { 0.1f, 0.2f }, topK: 3, filter: null);

        var exchangeLog = _server.LogEntries.First(e => e.RequestMessage?.Path == "/api/tenancy/vectordb/exchange-token/");
        var body = exchangeLog.RequestMessage!.Body!;
        Assert.Contains("collection=docs", body);
        Assert.DoesNotContain("name=docs", body);
    }

    [Fact]
    public async Task ExchangeResolvesPhysicalCollectionWhenPresent()
    {
        // A response carrying only the new "physical_collection" field (no "collection_name" at
        // all) must still resolve the real collection name correctly.
        _server
            .Given(Request.Create().WithPath("/api/tenancy/vectordb/exchange-token/").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                token = "t", access = "r", expires_at = 9999999999,
                physical_collection = "ns__docs", qdrant_url = _server.Url!,
            }));
        _server
            .Given(Request.Create().WithPath("/collections/ns__docs/points/query").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { result = new { points = Array.Empty<object>() } }));

        using var client = new VectorStoreClient("test-key", TimeSpan.FromSeconds(10), $"{_server.Url}/api/tenancy/vectordb/exchange-token/");

        // Would fail (404 against the wrong path, or a client-side exception) if resolution
        // fell back to requiring "collection_name" instead.
        await client.SearchAsync("docs", new[] { 0.1f, 0.2f }, topK: 3, filter: null);
    }

    [Fact]
    public async Task GetRecordedEmbedModelFallsBackToNullWhenAbsent()
    {
        _server
            .Given(Request.Create().WithPath("/api/tenancy/vectordb/exchange-token/").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                token = "t", access = "r", expires_at = 9999999999,
                collection_name = "ns__docs", qdrant_url = _server.Url!,
                // "embed_model" deliberately absent -- matches every real response for a
                // collection that predates this feature.
            }));

        using var client = new VectorStoreClient("test-key", TimeSpan.FromSeconds(10), $"{_server.Url}/api/tenancy/vectordb/exchange-token/");
        var recorded = await client.GetRecordedEmbedModelAsync("docs");

        Assert.Null(recorded);
    }

    [Fact]
    public async Task GetRecordedEmbedModelReturnsItWhenPresent()
    {
        _server
            .Given(Request.Create().WithPath("/api/tenancy/vectordb/exchange-token/").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                token = "t", access = "r", expires_at = 9999999999,
                collection_name = "ns__docs", qdrant_url = _server.Url!,
                embed_model = "custom/embedding-v2",
            }));

        using var client = new VectorStoreClient("test-key", TimeSpan.FromSeconds(10), $"{_server.Url}/api/tenancy/vectordb/exchange-token/");
        var recorded = await client.GetRecordedEmbedModelAsync("docs");

        Assert.Equal("custom/embedding-v2", recorded);
    }
}
