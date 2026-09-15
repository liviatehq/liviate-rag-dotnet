using Liviate.Rag;
using WireMock.Server;

namespace Liviate.Rag.Tests;

/// <summary>Spins up a local WireMock.Net server per test and a RagClient pointed at it --
/// mirrors the Python suite's pytest-httpserver-based conftest.py fixtures so the test suite
/// never depends on a live Liviate backend.</summary>
public sealed class MockServerFixture : IDisposable
{
    public WireMockServer Server { get; }
    public string BaseUrl => Server.Url!;

    public MockServerFixture()
    {
        Server = WireMockServer.Start();
    }

    public global::Liviate.Rag.RagClient CreateClient() => new(
        "test-key",
        new RagClientOptions
        {
            BaseUrl = BaseUrl,
            ExchangeUrl = $"{BaseUrl}/api/tenancy/vectordb/exchange-token/",
        });

    public void Dispose() => Server.Stop();
}
