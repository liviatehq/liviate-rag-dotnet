namespace Liviate.Rag.Models;

public sealed record EmbedUsage(int Tokens);

public sealed record EmbedResult(IReadOnlyList<IReadOnlyList<float>> Vectors, EmbedUsage Usage, Timing Timing);
