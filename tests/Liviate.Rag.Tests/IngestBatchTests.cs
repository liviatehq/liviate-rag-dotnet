using Liviate.Rag.Exceptions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Liviate.Rag.Tests;

/// <summary>Batch Ingest() behavior: partial failure vs. total failure. Per the API reference,
/// one failed item in a batch must not abort the others (aggregated into .Warnings/.PerSource[i]
/// .Warnings). But if EVERY item fails, nothing was ingested at all -- that must throw rather
/// than return a success-shaped zero-chunk result.</summary>
public class IngestBatchTests : IClassFixture<MockServerFixture>
{
    private readonly MockServerFixture _fixture;

    public IngestBatchTests(MockServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task BatchOfAllUnclassifiableItemsThrows()
    {
        using var client = _fixture.CreateClient();

        var exc = await Assert.ThrowsAsync<LiviateException>(
            () => client.IngestAsync(new[] { "not a path or url", "also not one" }, "docs"));
        Assert.Contains("All 2 item", exc.Message);
    }

    [Fact]
    public async Task BatchPartialFailureStillWarnsNotThrows()
    {
        var realFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(realFile, "some real text content");

            _fixture.Server
                .Given(Request.Create().WithPath("/v1/embeddings").UsingPost())
                .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
                {
                    @object = "list",
                    data = new[] { new { @object = "embedding", index = 0, embedding = new[] { 0.1f, 0.2f } } },
                    model = "liviate/embedding",
                    usage = new { prompt_tokens = 3, total_tokens = 3 },
                }));
            _fixture.Server
                .Given(Request.Create().WithPath("/api/tenancy/vectordb/exchange-token/").UsingPost())
                .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
                {
                    token = "scoped-jwt", access = "rw", expires_at = 9999999999,
                    collection_name = "testtenant__docs", qdrant_url = _fixture.BaseUrl,
                }));
            _fixture.Server
                .Given(Request.Create().WithPath("/collections/testtenant__docs/points").UsingPut())
                .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { }));

            using var client = _fixture.CreateClient();
            var result = await client.IngestAsync(new object[] { "not a path or url", realFile }, "docs");

            Assert.True(result.ChunksCreated > 0);
            Assert.Single(result.Warnings);
            Assert.Single(result.PointIds);
        }
        finally
        {
            File.Delete(realFile);
        }
    }

    [Fact]
    public async Task EmptyBatchDoesNotThrow()
    {
        using var client = _fixture.CreateClient();
        var result = await client.IngestAsync(Array.Empty<string>(), "docs");

        Assert.Equal(0, result.ChunksCreated);
        Assert.Empty(result.Warnings);
    }
}
