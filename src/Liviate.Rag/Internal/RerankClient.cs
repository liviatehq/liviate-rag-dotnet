using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Liviate.Rag.Models;

namespace Liviate.Rag.Internal;

/// <summary>
/// Rerank() implementation. Cohere-shaped response (results: [{index, relevance_score}]) --
/// confirmed against the live gateway, matching the Python reference implementation.
/// </summary>
internal static class RerankClient
{
    public const string DefaultModel = "liviate/rerank";

    public static async Task<RerankResult> RerankAsync(HttpClient gateway, string query, IReadOnlyList<string> documents, string model)
    {
        var sw = Stopwatch.StartNew();
        using var response = await gateway.PostAsJsonAsync("v1/rerank", new { model, query, documents });
        await HttpErrors.RaiseForStatusAsync(response);
        var elapsedMs = sw.Elapsed.TotalMilliseconds;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var results = doc.RootElement.GetProperty("results");

        var ranked = results.EnumerateArray()
            .Select(item => new RankedDocument
            {
                Text = documents[item.GetProperty("index").GetInt32()],
                Score = item.GetProperty("relevance_score").GetDouble(),
                Index = item.GetProperty("index").GetInt32(),
            })
            .OrderByDescending(d => d.Score)
            .ToList();

        return new RerankResult(ranked, new Usage { RerankDocs = documents.Count }, new Timing { RerankMs = elapsedMs });
    }
}
