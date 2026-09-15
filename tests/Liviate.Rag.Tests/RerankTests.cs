using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace Liviate.Rag.Tests;

/// <summary>Uses the assumed-but-confirmed Cohere-shaped response (results: [{index,
/// relevance_score}]) -- matches what was verified live against the real gateway.</summary>
public class RerankTests : IClassFixture<MockServerFixture>
{
    private readonly MockServerFixture _fixture;

    public RerankTests(MockServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task RerankReturnsBestFirst()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/rerank").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                results = new[]
                {
                    new { index = 1, relevance_score = 0.9 },
                    new { index = 0, relevance_score = 0.2 },
                },
            }));

        using var client = _fixture.CreateClient();
        var result = await client.RerankAsync("query", new[] { "doc a", "doc b" });

        Assert.Equal(new[] { "doc b", "doc a" }, result.Ranked.Select(d => d.Text));
        Assert.Equal(0.9, result.Ranked[0].Score);
        Assert.Equal(2, result.Usage.RerankDocs);
    }
}
