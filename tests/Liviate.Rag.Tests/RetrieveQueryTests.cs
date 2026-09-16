using System.Text.Json;
using Liviate.Rag.Exceptions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace Liviate.Rag.Tests;

/// <summary>RetrieveAsync/QueryAsync share Internal.RetrievalPipeline -- these tests assert
/// that shared path stays intact, and that a point with no text payload raises clearly rather
/// than silently degrading (regression test mirroring the Python suite's code-review fix).</summary>
public class RetrieveQueryTests : IClassFixture<MockServerFixture>
{
    private readonly MockServerFixture _fixture;

    public RetrieveQueryTests(MockServerFixture fixture) => _fixture = fixture;

    private void MockRetrievalChain(object[] points)
    {
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
                token = "scoped-jwt",
                access = "r",
                expires_at = 9999999999,
                collection_name = "testtenant__hotel-kirstine",
                qdrant_url = _fixture.BaseUrl,
            }));

        _fixture.Server
            .Given(Request.Create().WithPath("/collections/testtenant__hotel-kirstine/points/query").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { result = new { points } }));

        _fixture.Server
            .Given(Request.Create().WithPath("/v1/rerank").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                results = new[]
                {
                    new { index = 0, relevance_score = 0.95 },
                    new { index = 1, relevance_score = 0.4 },
                },
            }));
    }

    [Fact]
    public async Task RetrieveReturnsRankedSources()
    {
        MockRetrievalChain(new object[]
        {
            new { id = "1", score = 0.5, payload = new { text = "We have free parking.", metadata = new { page = 1 } } },
            new { id = "2", score = 0.3, payload = new { text = "Breakfast is included.", metadata = new { page = 2 } } },
        });

        using var client = _fixture.CreateClient();
        var result = await client.RetrieveAsync("Har I parkering?", "hotel-kirstine", topK: 5);

        Assert.Equal("We have free parking.", result.Sources[0].Text);
        Assert.Equal(3, result.Usage.EmbedTokens);
        Assert.Equal(2, result.Usage.RerankDocs);
    }

    [Fact]
    public async Task QueryReusesRetrieveAndAddsGeneration()
    {
        MockRetrievalChain(new object[]
        {
            new { id = "1", score = 0.5, payload = new { text = "We have free parking.", metadata = new { page = 1 } } },
            new { id = "2", score = 0.3, payload = new { text = "Breakfast is included.", metadata = new { page = 2 } } },
        });
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                id = "chatcmpl-1",
                @object = "chat.completion",
                model = "test/model",
                choices = new[]
                {
                    new { index = 0, message = new { role = "assistant", content = "Yes, free parking is available." }, finish_reason = "stop" },
                },
                usage = new { prompt_tokens = 20, completion_tokens = 8, total_tokens = 28 },
            }));

        using var client = _fixture.CreateClient();
        var result = await client.QueryAsync("Har I parkering?", "hotel-kirstine", "test/model");

        Assert.Equal("Yes, free parking is available.", result.Answer);
        Assert.Equal("We have free parking.", result.Sources[0].Text);
        Assert.Equal(8, result.Usage.GenerationTokens);
        Assert.True(result.Timing.GenerateMs >= 0);
    }

    [Fact]
    public async Task RetrieveThrowsOnPointWithNoTextPayload()
    {
        MockRetrievalChain(new object[]
        {
            new { id = "1", score = 0.5, payload = new { metadata = new { page = 1 } } },
        });

        using var client = _fixture.CreateClient();
        var exc = await Assert.ThrowsAsync<LiviateException>(() => client.RetrieveAsync("Har I parkering?", "hotel-kirstine", topK: 5));
        Assert.Contains("no 'text' in its payload", exc.Message);
    }

    [Fact]
    public async Task RetrievePassesCustomEmbedModelThrough()
    {
        // Regression test: RetrieveAsync/QueryAsync used to have no embedModel parameter at
        // all, so a collection ingested with a non-default embed model could never be queried
        // correctly -- the query was always embedded with the default model.
        MockRetrievalChain(Array.Empty<object>());

        using var client = _fixture.CreateClient();
        await client.RetrieveAsync("Har I parkering?", "hotel-kirstine", embedModel: "custom/embedding-v2");

        var embedRequest = _fixture.Server.LogEntries.Last(e => e.RequestMessage?.Path == "/v1/embeddings");
        var sentModel = JsonDocument.Parse(embedRequest.RequestMessage!.Body!).RootElement.GetProperty("model").GetString();
        Assert.Equal("custom/embedding-v2", sentModel);
    }

    [Fact]
    public async Task RetrieveFallsBackToDefaultWhenNoEmbedModelRecorded()
    {
        // Today's real exchange response for a pre-existing collection never includes
        // "embed_model" -- confirm the auto-resolve path falls back to the hardcoded default
        // rather than erroring or sending null as a model string.
        MockRetrievalChain(Array.Empty<object>());

        using var client = _fixture.CreateClient();
        await client.RetrieveAsync("query", "hotel-kirstine");

        var embedRequest = _fixture.Server.LogEntries.Last(e => e.RequestMessage?.Path == "/v1/embeddings");
        var sentModel = JsonDocument.Parse(embedRequest.RequestMessage!.Body!).RootElement.GetProperty("model").GetString();
        Assert.Equal("liviate/embedding", sentModel);
    }
}
