using System.Diagnostics;
using System.Text.Json;
using Liviate.Rag.Exceptions;
using Liviate.Rag.Models;

namespace Liviate.Rag.Internal;

/// <summary>
/// Shared retrieval pipeline used by both RetrieveAsync() and QueryAsync(). embed(query) ->
/// vector_search(collection) -> optional rerank(). Shared so the two methods can't drift out of
/// sync with each other.
/// </summary>
internal static class RetrievalPipeline
{
    public static async Task<RetrieveResult> RunAsync(
        VectorStoreClient vectorStore,
        HttpClient gateway,
        string query,
        string collection,
        int topK,
        object? filter,
        string embedModel = EmbedClient.DefaultModel,
        string? rerankModel = RerankClient.DefaultModel)
    {
        var embedResult = await EmbedClient.EmbedAsync(gateway, new[] { query }, embedModel);
        var embedVector = embedResult.Vectors[0];

        var sw = Stopwatch.StartNew();
        var hits = await vectorStore.SearchAsync(collection, embedVector, topK, filter);
        var retrieveMs = sw.Elapsed.TotalMilliseconds;

        var candidates = hits.Select((hit, i) => ToRankedDocument(hit, i, collection)).ToList();

        var usage = new Usage { EmbedTokens = embedResult.Usage.Tokens };
        var timing = new Timing { EmbedMs = embedResult.Timing.EmbedMs, RetrieveMs = retrieveMs };

        if (rerankModel is null || candidates.Count == 0)
        {
            return new RetrieveResult(candidates, usage, timing);
        }

        var rerankResult = await RerankClient.RerankAsync(gateway, query, candidates.Select(c => c.Text).ToList(), rerankModel);
        usage.RerankDocs = rerankResult.Usage.RerankDocs;
        timing.RerankMs = rerankResult.Timing.RerankMs;

        var sources = rerankResult.Ranked
            .Select(r => r with { Metadata = candidates[r.Index].Metadata })
            .ToList();

        return new RetrieveResult(sources, usage, timing);
    }

    private static RankedDocument ToRankedDocument(SearchHit hit, int index, string collection)
    {
        if (hit.Payload is not { } payload || payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("text", out var textEl) || textEl.ValueKind != JsonValueKind.String)
        {
            throw new LiviateException(
                $"Point '{hit.Id}' in collection '{collection}' has no 'text' in its payload -- " +
                "was it written outside of Ingest()? Retrieve()/Query() can't rank or return a " +
                "source with no text content.");
        }

        var metadata = new Dictionary<string, object?>();
        if (payload.TryGetProperty("metadata", out var metadataEl) && metadataEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in metadataEl.EnumerateObject())
            {
                metadata[prop.Name] = JsonElementToObject(prop.Value);
            }
        }

        return new RankedDocument
        {
            Text = textEl.GetString()!,
            Score = hit.Score,
            Index = index,
            Metadata = metadata,
        };
    }

    private static object? JsonElementToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.GetRawText(),
    };
}
