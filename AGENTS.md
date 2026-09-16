# AGENTS.md — liviate-rag-dotnet

Context for coding agents (Claude Code, Copilot, Cursor, etc.) working in this repo.
This is the .NET port of `liviate-rag-py` (Liviate's RAG pipeline SDK): ingest → embed →
retrieve → rerank → (optionally) generate. Read this before writing code against it.

## Setup

Current version: **0.1.2**. This is a young SDK (v0.1.x) — expect the API surface and the
gotchas below to shift between releases; check the installed version against this file if
something doesn't match.

```bash
dotnet add package Liviate.Rag                  # pin the version explicitly in real projects
dotnet test                                      # no network/credentials required (WireMock.Net)
dotnet run --project examples/HelloWorld         # needs LIVIATE_API_KEY, hits a real environment
dotnet run --project examples/Quickstart         # same
```

Auth: `new RagClient(apiKey: "...")` or set `LIVIATE_API_KEY` in the environment (get a key
at https://console.liviate.com). Don't hardcode keys.

Structural note if you're used to the Python SDK: there is no separate sync/async client
here. .NET's native `async`/`Task<T>` makes that split unnecessary — every I/O-bound method
on `RagClient` is `async Task<T>` (or `IAsyncEnumerable<string>` for streaming generation).

## Core surface

```csharp
using Liviate.Rag;

await using var client = new RagClient();
await client.IngestAsync("handbook.pdf", "hotel-kirstine");
var result = await client.QueryAsync("Har I parkering?", "hotel-kirstine", "anthropic/claude-sonnet-5");
// result.Answer / result.Sources / result.Usage / result.Timing

var context = await client.RetrieveAsync("Har I parkering?", "hotel-kirstine", topK: 5);  // BYO-LLM path
```

## Things that will bite you if you don't know them

- **`IngestAsync` detects source type from content, not filename.** A single call accepts a
  file, URL, raw text (`SourceType.Text`), or `Stream` — never branch on file extension
  yourself. Supported: PDF, DOCX, MD, TXT, CSV, JSON, HTML. OCR/scanned images are explicitly
  unsupported for v1 (`UnsupportedFileTypeException`, not a silent partial parse).
- **Whole-site ingestion:** pass a list of individual page URLs to `IngestAsync` — it already
  batches, so there's no separate site-crawl method (never was one shipped; the crawling
  itself is intentionally not this SDK's job — use whatever crawler you already have to
  produce the URL list).
- **Batch `IngestAsync` is partial-fail-tolerant.** If some items in a list fail but at
  least one succeeds, the call returns normally — check `result.Warnings`. Only a *total*
  batch failure throws `LiviateException`.
- **Embedding model auto-detection is live, but young — set it explicitly until you've
  confirmed it for your own collections.** `RetrieveAsync`/`QueryAsync` auto-resolve the
  `embedModel` a collection was ingested with when the backend has it on record (shipped
  server-side 2026-09-16). Collections created before that date won't have it recorded, and
  a mismatched embed model returns garbage or throws a dimension-mismatch error rather than
  a helpful message — so until you've verified auto-resolution for a given collection, keep
  passing `embedModel:` explicitly to `RetrieveAsync`/`QueryAsync` matching what you used at
  `IngestAsync` time.
- **Two distinct exception families — don't collapse them:**
  - `Liviate.Rag.Exceptions.LiviateException` (and subclasses `RateLimitException`,
    `UnsupportedFileTypeException`, `IngestTimeoutException`) = backend/runtime failures.
    `catch (LiviateException)` catches all of these, including failures inside the
    OpenAI-compatible gateway's generation step.
  - Plain `ArgumentException` = caller mistakes (unclassifiable source, calling
    `DeleteAsync` with both or neither of `ids`/`filter`). These are bugs in the calling
    code — handle separately, don't swallow them under `catch (LiviateException)`.
- **`DeleteAsync` takes exactly one of `ids` or `filter`, never both/neither:**
  ```csharp
  await client.DeleteAsync("hotel-kirstine", ids: result.PointIds);
  await client.DeleteAsync("hotel-kirstine", filter: new { must = new[] { /* ... */ } });
  ```

## When building on this SDK

- Prefer `client.QueryAsync()` when the SDK should also call the LLM; use
  `client.RetrieveAsync()` when the caller wants ranked context only and will call its own
  LLM.
- Wrap ingestion/query calls in `try { } catch (LiviateException) { }` for user-facing error
  handling; let `ArgumentException` surface during development as a signal of incorrect
  usage.
- Collection naming and `embedModel` choice should be decided once per project and reused
  consistently — mixing embed models across ingest/query calls into the same collection is
  the most common source of silent bugs right now (see above).
