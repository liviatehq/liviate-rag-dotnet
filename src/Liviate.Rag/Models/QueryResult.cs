namespace Liviate.Rag.Models;

public sealed record QueryResult(string Answer, IReadOnlyList<RankedDocument> Sources, Usage Usage, Timing Timing);
