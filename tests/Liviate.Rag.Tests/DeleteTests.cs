using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Liviate.Rag.Tests;

public class DeleteTests : IClassFixture<MockServerFixture>
{
    private readonly MockServerFixture _fixture;

    public DeleteTests(MockServerFixture fixture) => _fixture = fixture;

    private void MockExchange()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/api/tenancy/vectordb/exchange-token/").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                token = "scoped-jwt", access = "rw", expires_at = 9999999999,
                collection_name = "testtenant__docs", qdrant_url = _fixture.BaseUrl,
            }));
    }

    [Fact]
    public async Task DeleteByIds()
    {
        MockExchange();
        _fixture.Server
            .Given(Request.Create().WithPath("/collections/testtenant__docs/points/delete").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { result = new { status = "acknowledged" } }));

        using var client = _fixture.CreateClient();
        await client.DeleteAsync("docs", ids: new[] { "a", "b" });
    }

    [Fact]
    public async Task DeleteByFilter()
    {
        MockExchange();
        _fixture.Server
            .Given(Request.Create().WithPath("/collections/testtenant__docs/points/delete").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { result = new { status = "acknowledged" } }));

        using var client = _fixture.CreateClient();
        await client.DeleteAsync("docs", filter: new { must = new[] { new { key = "metadata.tag", match = new { value = "x" } } } });
    }

    [Fact]
    public async Task DeleteRequiresExactlyOneOfIdsOrFilter()
    {
        using var client = _fixture.CreateClient();

        await Assert.ThrowsAsync<ArgumentException>(() => client.DeleteAsync("docs"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.DeleteAsync("docs", ids: new[] { "a" }, filter: new { }));
    }
}
