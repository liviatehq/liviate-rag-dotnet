namespace Liviate.Rag.Models;

public sealed record IngestResult
{
    public required int ChunksCreated { get; init; }
    public required string SourceType { get; init; }
    public required string Collection { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Populated only for batch ingest (one entry per input item).</summary>
    public IReadOnlyList<IngestResult>? PerSource { get; init; }
}
