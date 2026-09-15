using Liviate.Rag.Exceptions;
using Liviate.Rag.Ingest;
using Liviate.Rag.Internal;
using Liviate.Rag.Models;

namespace Liviate.Rag;

/// <summary>
/// Client for Liviate's RAG stack: ingest, embed, retrieve, rerank, and (optionally) generate.
///
/// Unlike the Python reference implementation, there is no separate sync/async client here --
/// .NET's native async/<c>Task&lt;T&gt;</c> makes that split unnecessary. Every I/O-bound
/// method is <c>async Task&lt;T&gt;</c> (or <c>IAsyncEnumerable&lt;string&gt;</c> for streaming
/// generation).
/// </summary>
public sealed class RagClient : IDisposable, IAsyncDisposable
{
    private const double DefaultIngestTimeoutSeconds = 120;

    private readonly HttpClient _gateway;
    private readonly VectorStoreClient _vectorStore;

    public RagClient(string? apiKey = null, RagClientOptions? options = null)
    {
        options ??= new RagClientOptions();
        var resolvedKey = RagClientOptions.ResolveApiKey(apiKey);
        _gateway = HttpClients.BuildGatewayClient(resolvedKey, options.BaseUrl, options.Timeout);
        _vectorStore = new VectorStoreClient(resolvedKey, options.Timeout, options.ExchangeUrl);
    }

    public void Dispose()
    {
        _gateway.Dispose();
        _vectorStore.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _gateway.Dispose();
        await _vectorStore.DisposeAsync();
    }

    // -- ingest ------------------------------------------------------

    /// <summary>
    /// Extracts text, chunks it, embeds each chunk, and upserts it into the vector store --
    /// entirely client-side. Awaits completion; throws <see cref="IngestTimeoutException"/> if
    /// it exceeds <paramref name="timeout"/> (default 120s). For a non-blocking call, use
    /// <see cref="StartIngest"/> instead.
    /// </summary>
    public Task<IngestResult> IngestAsync(
        object source,
        string collection,
        SourceType sourceType = SourceType.Auto,
        IReadOnlyDictionary<string, object?>? metadata = null,
        TimeSpan? timeout = null,
        string embedModel = EmbedClient.DefaultModel)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(DefaultIngestTimeoutSeconds);
        return RunIngestWithTimeoutAsync(source, collection, sourceType, metadata, embedModel, effectiveTimeout);
    }

    /// <summary>
    /// Non-blocking counterpart to <see cref="IngestAsync"/> -- schedules the same
    /// extract/chunk/embed/upsert pipeline as a background <see cref="Task"/> and returns
    /// immediately. There is no server-side ingest job; the returned <see cref="IngestJob"/>
    /// just lets the caller observe or await that background Task later.
    /// </summary>
    public IngestJob StartIngest(
        object source,
        string collection,
        SourceType sourceType = SourceType.Auto,
        IReadOnlyDictionary<string, object?>? metadata = null,
        TimeSpan? timeout = null,
        string embedModel = EmbedClient.DefaultModel)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(DefaultIngestTimeoutSeconds);
        var task = Task.Run(() => RunIngestWithTimeoutAsync(source, collection, sourceType, metadata, embedModel, effectiveTimeout));
        return new IngestJob(Guid.NewGuid().ToString(), collection, task);
    }

    private async Task<IngestResult> RunIngestWithTimeoutAsync(
        object source, string collection, SourceType sourceType,
        IReadOnlyDictionary<string, object?>? metadata, string embedModel, TimeSpan timeout)
    {
        var ingestTask = IngestPipeline.RunAsync(_gateway, _vectorStore, source, collection, sourceType, metadata, embedModel);
        var delayTask = Task.Delay(timeout);
        var completed = await Task.WhenAny(ingestTask, delayTask);
        if (completed == delayTask)
        {
            throw new IngestTimeoutException();
        }
        return await ingestTask;
    }

    /// <summary>
    /// Not yet implemented. Whole-site crawling (following internal links, respecting
    /// robots.txt, paging through up to maxPages) is a genuinely separate feature from
    /// Ingest()'s single-source path -- it needs its own real engineering (crawl frontier,
    /// politeness/rate limiting, dedup), not a rushed version bolted onto Ingest()'s pipeline.
    /// Throws rather than pretending to support this -- matches the Python reference
    /// implementation's status.
    /// </summary>
    public Task<IngestResult> IngestSiteAsync(string source, string collection, int maxPages = 50, IReadOnlyDictionary<string, object?>? metadata = null)
    {
        throw new NotImplementedException(
            "IngestSiteAsync() is not yet implemented. Use IngestAsync() with a list of " +
            "individual page URLs in the meantime -- it already accepts a batch of sources in one call.");
    }

    // -- embed / rerank ------------------------------------------------

    public Task<EmbedResult> EmbedAsync(IReadOnlyList<string> texts, string model = EmbedClient.DefaultModel) =>
        EmbedClient.EmbedAsync(_gateway, texts, model);

    public Task<RerankResult> RerankAsync(string query, IReadOnlyList<string> documents, string model = RerankClient.DefaultModel) =>
        RerankClient.RerankAsync(_gateway, query, documents, model);

    // -- retrieve / query ------------------------------------------------

    public Task<RetrieveResult> RetrieveAsync(
        string query, string collection, int topK = 5, object? filter = null, string? rerankModel = RerankClient.DefaultModel) =>
        RetrievalPipeline.RunAsync(_vectorStore, _gateway, query, collection, topK, filter, rerankModel: rerankModel);

    /// <summary>
    /// <paramref name="model"/> has no Liviate default: generation is deliberately kept out of
    /// the bundled RAG product (see project brief) so the caller always names their own
    /// Inference model explicitly.
    /// </summary>
    public async Task<QueryResult> QueryAsync(string query, string collection, string model, int topK = 5, object? filter = null)
    {
        var retrieval = await RetrieveAsync(query, collection, topK, filter);
        var (answer, genUsage, genTiming) = await GenerateClient.GenerateAsync(_gateway, query, retrieval.Sources, model);

        var usage = retrieval.Usage with { GenerationTokens = genUsage.GenerationTokens };
        var timing = retrieval.Timing with { GenerateMs = genTiming.GenerateMs };
        return new QueryResult(answer, retrieval.Sources, usage, timing);
    }

    /// <summary>Streaming variant of QueryAsync -- yields answer text chunks as they arrive.</summary>
    public async IAsyncEnumerable<string> QueryStreamAsync(
        string query, string collection, string model, int topK = 5, object? filter = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var retrieval = await RetrieveAsync(query, collection, topK, filter);
        await foreach (var chunk in GenerateClient.GenerateStreamAsync(_gateway, query, retrieval.Sources, model, cancellationToken))
        {
            yield return chunk;
        }
    }
}
