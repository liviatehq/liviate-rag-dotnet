using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Liviate.Rag.Models;

namespace Liviate.Rag.Internal;

/// <summary>
/// Embed() implementation -- talks to the LiteLLM-fronted embedding backend via the
/// OpenAI-compatible /v1/embeddings shape.
/// </summary>
internal static class EmbedClient
{
    public const string DefaultModel = "liviate/embedding";

    public static async Task<EmbedResult> EmbedAsync(HttpClient gateway, IReadOnlyList<string> texts, string model)
    {
        var sw = Stopwatch.StartNew();
        using var response = await gateway.PostAsJsonAsync("v1/embeddings", new { model, input = texts });
        await HttpErrors.RaiseForStatusAsync(response);
        var elapsedMs = sw.Elapsed.TotalMilliseconds;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        var vectors = root.GetProperty("data")
            .EnumerateArray()
            .Select(item => (IReadOnlyList<float>)item.GetProperty("embedding")
                .EnumerateArray()
                .Select(v => v.GetSingle())
                .ToList())
            .ToList();

        var tokens = root.TryGetProperty("usage", out var usage) && usage.TryGetProperty("total_tokens", out var tot)
            ? tot.GetInt32()
            : 0;

        return new EmbedResult(vectors, new EmbedUsage(tokens), new Timing { EmbedMs = elapsedMs });
    }
}
