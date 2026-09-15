namespace Liviate.Rag.Models;

public sealed record RankedDocument
{
    public required string Text { get; init; }
    public required double Score { get; init; }
    public required int Index { get; init; }
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();
}
