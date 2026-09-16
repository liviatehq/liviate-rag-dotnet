namespace Liviate.Rag.Models;

public sealed record IngestResult
{
    public required int ChunksCreated { get; init; }
    public required string SourceType { get; init; }
    public required string Collection { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Populated only for batch ingest (one entry per input item).</summary>
    public IReadOnlyList<IngestResult>? PerSource { get; init; }

    /// <summary>
    /// Vector-store point IDs this ingest wrote, so the content can later be removed with
    /// <c>client.DeleteAsync(collection, ids: result.PointIds)</c>.
    /// </summary>
    public IReadOnlyList<string> PointIds { get; init; } = Array.Empty<string>();
}
