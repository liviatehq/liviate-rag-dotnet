using Liviate.Rag.Exceptions;
using Liviate.Rag.Internal;
using Liviate.Rag.Models;

namespace Liviate.Rag.Ingest;

/// <summary>
/// Ingest pipeline: extract -> chunk -> embed -> upsert. Fully client-side -- there is no
/// server-side ingest job. A source (file/text/url/stream) is turned into plain text
/// (TextExtractor), split into chunks (Chunker), embedded via the same EmbedClient the rest of
/// the package already uses, then written directly to the vector store via
/// VectorStoreClient.UpsertAsync (the same token-exchange path SearchAsync uses).
/// </summary>
public static class IngestPipeline
{
    public static async Task<IngestResult> RunAsync(
        HttpClient gateway,
        VectorStoreClient vectorStore,
        object source,
        string collection,
        SourceType sourceType,
        IReadOnlyDictionary<string, object?>? metadata,
        string embedModel)
    {
        var classification = SourceClassifier.Classify(source, sourceType);
        if (classification.Kind == SourceKind.Batch)
        {
            return await IngestBatchAsync(gateway, vectorStore, (List<object>)classification.Value, collection, sourceType, metadata, embedModel);
        }
        return await IngestOneAsync(gateway, vectorStore, classification, collection, metadata, embedModel);
    }

    private static async Task<(string Text, string SourceType)> TextAndTypeForAsync(Classification classification)
    {
        switch (classification.Kind)
        {
            case SourceKind.Text:
                return ((string)classification.Value, "text");

            case SourceKind.File:
            {
                var fileInfo = (FileInfo)classification.Value;
                var data = await File.ReadAllBytesAsync(fileInfo.FullName);
                var sourceType = FileTypeSniffer.Sniff(data);
                return (TextExtractor.ExtractText(data, sourceType), sourceType);
            }

            case SourceKind.Stream:
            {
                var stream = (Stream)classification.Value;
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                var data = ms.ToArray();
                var sourceType = FileTypeSniffer.Sniff(data);
                return (TextExtractor.ExtractText(data, sourceType), sourceType);
            }

            case SourceKind.Url:
                return await FetchUrlTextAsync((string)classification.Value);

            default:
                throw new InvalidOperationException($"Unreachable classification kind: {classification.Kind}");
        }
    }

    private static async Task<(string Text, string SourceType)> FetchUrlTextAsync(string url)
    {
        using var web = new HttpClient();
        using var response = await web.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadAsByteArrayAsync();
        var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";

        if (contentType is "" or "text/html")
        {
            return (TextExtractor.ExtractText(data, "html"), "html");
        }
        var sourceType = FileTypeSniffer.Sniff(data);
        return (TextExtractor.ExtractText(data, sourceType), sourceType);
    }

    private static async Task<IngestResult> IngestOneAsync(
        HttpClient gateway, VectorStoreClient vectorStore, Classification classification,
        string collection, IReadOnlyDictionary<string, object?>? metadata, string embedModel)
    {
        var (text, sourceType) = await TextAndTypeForAsync(classification);
        var chunks = Chunker.ChunkText(text);
        if (chunks.Count == 0)
        {
            return new IngestResult
            {
                ChunksCreated = 0, SourceType = sourceType, Collection = collection,
                Warnings = new[] { "no extractable text content" },
            };
        }

        var embedResult = await EmbedClient.EmbedAsync(gateway, chunks, embedModel);
        var pointIds = chunks.Select(_ => Guid.NewGuid().ToString()).ToList();
        var points = new List<VectorStorePoint>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            points.Add(new VectorStorePoint(
                Id: pointIds[i],
                Vector: embedResult.Vectors[i],
                Payload: new Dictionary<string, object?> { ["text"] = chunks[i], ["metadata"] = metadata ?? new Dictionary<string, object?>() }
            ));
        }

        await vectorStore.UpsertAsync(collection, points, embedModel);
        return new IngestResult
        {
            ChunksCreated = chunks.Count, SourceType = sourceType, Collection = collection, PointIds = pointIds,
        };
    }

    private static async Task<IngestResult> IngestBatchAsync(
        HttpClient gateway, VectorStoreClient vectorStore, List<object> items, string collection,
        SourceType sourceType, IReadOnlyDictionary<string, object?>? metadata, string embedModel)
    {
        var perSource = new List<IngestResult>();
        var warnings = new List<string>();
        var totalChunks = 0;

        foreach (var item in items)
        {
            IngestResult result;
            try
            {
                var itemClassification = SourceClassifier.Classify(item, sourceType);
                result = await IngestOneAsync(gateway, vectorStore, itemClassification, collection, metadata, embedModel);
            }
            catch (Exception exc) when (exc is ArgumentException or LiviateException or HttpRequestException)
            {
                result = new IngestResult { ChunksCreated = 0, SourceType = "unknown", Collection = collection, Warnings = new[] { exc.Message } };
            }
            perSource.Add(result);
            warnings.AddRange(result.Warnings);
            totalChunks += result.ChunksCreated;
        }

        // If every item in a non-empty batch failed, nothing was ingested -- that's a total
        // failure, not the "one bad item shouldn't abort the others" partial-failure case the
        // per-item warning behavior above exists for. Throw instead of returning a
        // success-shaped zero-chunk result a caller could easily miss without inspecting
        // .Warnings.
        if (items.Count > 0 && totalChunks == 0 && warnings.Count > 0)
        {
            throw new LiviateException(
                $"All {items.Count} item(s) in this batch failed to ingest -- nothing was written. " +
                $"First error: {warnings[0]}");
        }

        var pointIds = perSource.SelectMany(r => r.PointIds).ToList();
        return new IngestResult
        {
            ChunksCreated = totalChunks, SourceType = "batch", Collection = collection,
            Warnings = warnings, PerSource = perSource, PointIds = pointIds,
        };
    }
}
