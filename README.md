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
detecting which one automatically. See `Ingest/SourceClassifier.cs` and
`Ingest/IngestPipeline.cs` for the exact detection order and supported file
types (`.pdf`, `.docx`, `.md`, `.txt`, `.csv`, `.json`, `.html`).

For a non-blocking call, use `StartIngest` instead — it returns an
`IngestJob` immediately rather than awaiting completion.

Whole-site crawling (`IngestSiteAsync`) is not yet implemented — it throws
`NotImplementedException`, matching the Python reference implementation's
status. Use `IngestAsync` with a list of individual page URLs instead.

## Development

```bash
dotnet test                          # no network/credentials required (WireMock.Net)
dotnet run --project examples/HelloWorld    # needs LIVIATE_API_KEY
dotnet run --project examples/Quickstart    # needs LIVIATE_API_KEY
dotnet pack src/Liviate.Rag           # builds the NuGet package
```

## Status

Built and verified live against production (not just against the mock test
suite) — `Ingest` → `Retrieve` → `Query` all confirmed working end-to-end.
Unlike the Python SDK's original v1 (which had to guess at several backend
contracts), this port was built directly against contracts already
confirmed live: the token-exchange vector-store access pattern, the
form-encoded exchange request body, and the Cohere-shaped rerank response.
