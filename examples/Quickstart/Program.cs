// Liviate.Rag quickstart.
//
// Run with:
//
//     LIVIATE_API_KEY=sk-... dotnet run --project examples/Quickstart
//
// The key needs:
//   - the liviate/embedding and liviate/rerank models on its allow-list (or an empty models
//     list, which means "all models")
//   - Vector Database scope enabled, with the "quickstart" collection allowed (or an empty
//     collections list, which means "all collections")
//   - a chat model on its allow-list if you want the generate step -- this example uses
//     deepinfra/deepseek-ai/DeepSeek-V4-Flash
//
// Create a key with this shape from the console: Inference -> API Keys -> Generate key. The
// key's own error message lists exactly which models it's allowed to use if this one isn't
// available to you (a 403 naming "key not allowed to access model").

using Liviate.Rag;

// Locale-independent output -- otherwise numbers in the printed Usage/Timing records (and the
// score below) render with a comma decimal separator on many non-US systems.
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

const string Collection = "quickstart";
const string GenerationModel = "deepinfra/deepseek-ai/DeepSeek-V4-Flash";

await using var client = new RagClient();

Console.WriteLine($"Ingesting into collection '{Collection}'...");
var ingestResult = await client.IngestAsync(
    "Liviate offers a managed vector database, embedding, and reranking as separately billed products.",
    Collection,
    Liviate.Rag.Ingest.SourceType.Text);
Console.WriteLine($"  -> {ingestResult.ChunksCreated} chunk(s) created ({ingestResult.SourceType})");

// A collection is created automatically the first time you Ingest() into a name that doesn't
// exist yet -- no separate "create the collection first" step needed.

Console.WriteLine("\nRetrieveAsync() -- ranked context only, no generation:");
var retrieval = await client.RetrieveAsync("What does Liviate offer?", Collection, topK: 3);
for (var i = 0; i < retrieval.Sources.Count; i++)
{
    var source = retrieval.Sources[i];
    var score = source.Score.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
    Console.WriteLine($"  {i + 1}. (score={score}) {source.Text}");
}

Console.WriteLine($"\nQueryAsync() -- retrieval + generation via {GenerationModel}:");
var result = await client.QueryAsync("What does Liviate offer?", Collection, GenerationModel);
Console.WriteLine($"  answer: {result.Answer}");
Console.WriteLine($"  usage:  {result.Usage}");
Console.WriteLine($"  timing: {result.Timing}");
