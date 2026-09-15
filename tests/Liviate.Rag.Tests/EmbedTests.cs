using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace Liviate.Rag.Tests;

public class EmbedTests : IClassFixture<MockServerFixture>
{
    private readonly MockServerFixture _fixture;

    public EmbedTests(MockServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task EmbedReturnsVectorsAndUsage()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/embeddings").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                @object = "list",
                data = new[] { new { @object = "embedding", index = 0, embedding = new[] { 0.1f, 0.2f, 0.3f } } },
                model = "liviate/embedding",
                usage = new { prompt_tokens = 4, total_tokens = 4 },
            }));

        using var client = _fixture.CreateClient();
        var result = await client.EmbedAsync(new[] { "hello world" });

        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, result.Vectors[0]);
        Assert.Equal(4, result.Usage.Tokens);
        Assert.True(result.Timing.EmbedMs >= 0);
    }
}
