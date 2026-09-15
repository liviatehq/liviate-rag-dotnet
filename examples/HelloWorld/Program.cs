// The smallest possible Liviate.Rag program -- just proves your install and API key work. No
// collection, no model string, nothing else to set up.
//
// Run with:
//
//     LIVIATE_API_KEY=sk-... dotnet run --project examples/HelloWorld
//
// For the full Ingest -> Retrieve -> Query pipeline, see examples/Quickstart.

using Liviate.Rag;

await using var client = new RagClient();
var result = await client.EmbedAsync(new[] { "Hello, world!" });

Console.WriteLine($"Embedded 1 text into a {result.Vectors[0].Count}-dimensional vector.");
Console.WriteLine($"Tokens used: {result.Usage.Tokens}");
