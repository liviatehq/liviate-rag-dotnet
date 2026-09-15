namespace Liviate.Rag.Models;

/// <summary>
/// Billing/observability breakdown. Fields default to 0 so a result from a single step (e.g.
/// standalone Rerank()) doesn't need to populate fields that step didn't touch.
/// </summary>
public sealed record Usage
{
    public int EmbedTokens { get; set; }
    public int RerankDocs { get; set; }
    public int GenerationTokens { get; set; }
}
