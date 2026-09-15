using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Liviate.Rag.Models;

namespace Liviate.Rag.Internal;

/// <summary>
/// Generate() -- the final step of Query(). Routes through whatever model the caller names on
/// the separate Inference product (see RagClient.QueryAsync's required model parameter) via
/// chat completions on the same OpenAI-compatible gateway used for embeddings.
/// </summary>
internal static class GenerateClient
{
    private const string SystemPrompt =
        "Answer the user's question using only the provided context. If the context doesn't " +
        "contain the answer, say so.";

    private static object[] BuildMessages(string query, IReadOnlyList<RankedDocument> sources)
    {
        var context = string.Join("\n\n", sources.Select((s, i) => $"[{i + 1}] {s.Text}"));
        return new object[]
        {
            new { role = "system", content = SystemPrompt },
            new { role = "user", content = $"Context:\n{context}\n\nQuestion: {query}" },
        };
    }

    public static async Task<(string Answer, Usage Usage, Timing Timing)> GenerateAsync(
        HttpClient gateway, string query, IReadOnlyList<RankedDocument> sources, string model)
    {
        var sw = Stopwatch.StartNew();
        using var response = await gateway.PostAsJsonAsync("v1/chat/completions", new { model, messages = BuildMessages(query, sources) });
        await HttpErrors.RaiseForStatusAsync(response);
        var elapsedMs = sw.Elapsed.TotalMilliseconds;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        var answer = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        var tokens = root.TryGetProperty("usage", out var usage) && usage.TryGetProperty("completion_tokens", out var t)
            ? t.GetInt32()
            : 0;

        return (answer, new Usage { GenerationTokens = tokens }, new Timing { GenerateMs = elapsedMs });
    }

    public static async IAsyncEnumerable<string> GenerateStreamAsync(
        HttpClient gateway, string query, IReadOnlyList<RankedDocument> sources, string model,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = JsonContent.Create(new { model, messages = BuildMessages(query, sources), stream = true }),
        };
        using var response = await gateway.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await HttpErrors.RaiseForStatusAsync(response);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }
            var payload = line["data: ".Length..];
            if (payload == "[DONE]")
            {
                yield break;
            }

            using var doc = JsonDocument.Parse(payload);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
            {
                continue;
            }
            if (choices[0].TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("content", out var contentEl) &&
                contentEl.ValueKind == JsonValueKind.String)
            {
                var chunk = contentEl.GetString();
                if (!string.IsNullOrEmpty(chunk))
                {
                    yield return chunk;
                }
            }
        }
    }
}
