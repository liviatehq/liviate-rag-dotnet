namespace Liviate.Rag.Models;

public sealed record RetrieveResult(IReadOnlyList<RankedDocument> Sources, Usage Usage, Timing Timing);
