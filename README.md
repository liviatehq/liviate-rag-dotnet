# Liviate.Rag

Client SDK for Liviate's RAG stack — ingest, embed, retrieve, rerank, and
(optionally) generate — in a few lines of C#.

This is the .NET port of [`liviate-rag-py`](https://github.com/liviatehq/liviate-rag-py),
the Python reference implementation. One structural difference from the
Python SDK: there is no separate sync/async client here. .NET's native
`async`/`Task<T>` makes that split unnecessary — every I/O-bound method on
`RagClient` is `async Task<T>` (or `IAsyncEnumerable<string>` for streaming
generation).

```bash
dotnet add package Liviate.Rag
```

## Quickstart

```csharp
using Liviate.Rag;

await using var client = new RagClient(); // or new RagClient(apiKey: "sk-...")

await client.IngestAsync("handbook.pdf", "hotel-kirstine");

var result = await client.QueryAsync("Har I parkering?", "hotel-kirstine", "anthropic/claude-sonnet-5");
Console.WriteLine(result.Answer);
Console.WriteLine(result.Usage);
Console.WriteLine(result.Timing);
```

Want ranked context only, and to call your own LLM?

```csharp
var context = await client.RetrieveAsync("Har I parkering?", "hotel-kirstine", topK: 5);
```

## Ingest

`IngestAsync` handles a single file, URL, text string, or `Stream` —
detecting which one automatically from content, never a filename
extension. See `Ingest/SourceClassifier.cs` for the exact detection order.

### Supported file types

| Type | Extension |
|---|---|
| PDF | `.pdf` |
| Word | `.docx` |
| Markdown | `.md` |
| Plain text | `.txt` |
| CSV | `.csv` |
| JSON | `.json` |
| HTML | `.html` |

Plus raw text (`SourceType.Text`) and URLs (a single page is scraped or
downloaded automatically depending on its content type).

OCR / scanned images are explicitly out of scope for v1 —
`IngestAsync` throws `UnsupportedFileTypeException` rather than failing
silently or half-parsing.

For a non-blocking call, use `StartIngest` instead — it returns an
`IngestJob` immediately rather than awaiting completion.

Whole-site crawling isn't part of this SDK — that's a genuinely different
problem (robots.txt compliance, politeness/rate limiting, avoiding crawl
traps) than ingesting sources you already have. Crawl with whatever tool
you already use, then pass the resulting URL list to `IngestAsync` — it
already accepts a batch of sources in one call.

## Development

```bash
dotnet test                          # no network/credentials required (WireMock.Net)
dotnet run --project examples/HelloWorld    # needs LIVIATE_API_KEY
dotnet run --project examples/Quickstart    # needs LIVIATE_API_KEY
dotnet pack src/Liviate.Rag           # builds the NuGet package
```
