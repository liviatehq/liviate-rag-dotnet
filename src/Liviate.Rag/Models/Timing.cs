namespace Liviate.Rag.Models;

public sealed record Timing
{
    public double EmbedMs { get; set; }
    public double RetrieveMs { get; set; }
    public double RerankMs { get; set; }
    public double GenerateMs { get; set; }
}
