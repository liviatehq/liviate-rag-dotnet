namespace Liviate.Rag.Models;

public sealed record RerankResult(IReadOnlyList<RankedDocument> Ranked, Usage Usage, Timing Timing);
