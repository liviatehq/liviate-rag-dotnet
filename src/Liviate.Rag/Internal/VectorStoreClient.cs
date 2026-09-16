using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Liviate.Rag.Exceptions;

namespace Liviate.Rag.Internal;

public sealed record VectorStorePoint(string Id, IReadOnlyList<float> Vector, IReadOnlyDictionary<string, object?> Payload);

public sealed record SearchHit(string Id, double Score, JsonElement? Payload);

/// <summary>
/// Direct access to the managed vector database's own data-plane API.
///
/// This SDK holds exactly one long-lived secret: the caller's api key. It is never presented to
/// the vector store directly; the vector store validates a different, short-lived credential
/// that this class obtains by exchanging the api key with Liviate's own token-exchange endpoint,
/// then caches in memory (per collection, per access level) until shortly before it expires.
/// This is the only place in the whole client where that exchange happens; every other class
/// (embed, rerank, generate) sends the api key straight through as a normal bearer token.
///
/// The exchange response also carries the vector store's real, tenant-namespaced collection
/// name (distinct from the logical collection the caller passes) and its real data-plane URL.
/// Both are used as-is rather than guessed/hardcoded here.
///
/// Never reference the underlying vector-store technology by name in this class, or anywhere
/// else in the package -- see project brief. Even the exchange response's own field names
/// (which do name it) are never echoed into an exception message a caller could see -- see
/// <see cref="Field"/> below.
///
/// Confirmed live 2026-09-16 against the real backend (mirrors the Python reference
/// implementation's equivalent fix): the exchange endpoint accepts "collection" as the current
/// request field name (keeping "name" as a permanent alias -- either works), records the
/// embed_model the first time a collection is auto-created and returns it as "embed_model" on
/// every later exchange, and prefers "physical_collection" over the older "collection_name" in
/// its response (both are still sent today, but only physical_collection is guaranteed going
/// forward).
/// </summary>
public sealed class VectorStoreClient : IDisposable, IAsyncDisposable
{
    // Refresh this many ms before the exchanged credential's real expiry, so an in-flight
    // request can never race a credential that expires mid-call.
    private const double RefreshMarginMs = 30_000;

    private readonly string _apiKey;
    private readonly string _exchangeUrl;
    private readonly HttpClient _exchangeHttp;
    private readonly TimeSpan _timeout;
    private readonly ConcurrentDictionary<string, HttpClient> _dataHttpByBase = new();
    private readonly ConcurrentDictionary<(string Collection, string Access), CachedGrant> _cache = new();

    // One semaphore per (collection, access) key so concurrent callers hitting a cold/expired
    // cache entry for the same key share a single exchange call instead of each firing their
    // own -- e.g. Task.WhenAll over several RetrieveAsync calls against the same collection.
    private readonly ConcurrentDictionary<(string Collection, string Access), SemaphoreSlim> _locks = new();

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public VectorStoreClient(string apiKey, TimeSpan timeout, string? exchangeUrl = null)
    {
        _apiKey = apiKey;
        _exchangeUrl = exchangeUrl ?? "https://console.liviate.com/api/tenancy/vectordb/exchange-token/";
        _timeout = timeout;
        _exchangeHttp = new HttpClient { Timeout = timeout };
    }

    public void Dispose()
    {
        _exchangeHttp.Dispose();
        foreach (var client in _dataHttpByBase.Values)
        {
            client.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.CompletedTask;
    }

    private HttpClient DataHttpFor(string baseUrl) =>
        _dataHttpByBase.GetOrAdd(baseUrl, url => new HttpClient { BaseAddress = new Uri(url), Timeout = _timeout });

    private async Task<CachedGrant> GrantForAsync(
        string collection, string access, int? vectorSize = null, string? embedModel = null)
    {
        var key = (collection, access);
        if (_cache.TryGetValue(key, out var cached) && cached.Usable(_clock))
        {
            return cached;
        }

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            // Re-check: another caller may have already refreshed this key while we were
            // waiting for the lock.
            if (_cache.TryGetValue(key, out cached) && cached.Usable(_clock))
            {
                return cached;
            }

            var data = new Dictionary<string, string> { ["collection"] = collection, ["access"] = access };
            if (vectorSize is not null)
            {
                // Lets the exchange endpoint auto-create the collection on first write -- an
                // ingest caller already knows its embedding dimension (it just computed the
                // vectors), so there's no need to require a separate "create the collection
                // first" step through the console UI before a customer's very first Ingest()
                // can succeed.
                data["vector_size"] = vectorSize.Value.ToString();
            }
            if (embedModel is not null)
            {
                // Recorded against the collection the first time it's auto-created, so a later
                // RetrieveAsync/QueryAsync can resolve it automatically -- see
                // GetRecordedEmbedModelAsync below.
                data["embed_model"] = embedModel;
            }

            // NOTE: this endpoint genuinely expects form-encoded data, unlike every other
            // Liviate API call in this package (confirmed live against production: a JSON body
            // gets a 400 here). Don't "fix" this to JSON again without re-confirming live.
            using var request = new HttpRequestMessage(HttpMethod.Post, _exchangeUrl)
            {
                Content = new FormUrlEncodedContent(data),
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

            using var response = await _exchangeHttp.SendAsync(request);
            await HttpErrors.RaiseForStatusAsync(response);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            var root = body.RootElement;

            var expiresAtUnix = Field(root, "expires_at", "credential expiry").GetDouble();
            // expires_at is a Unix timestamp (server clock), not a duration -- convert to an
            // elapsed-clock-relative TTL once here so CachedGrant never has to compare against
            // wall-clock time (which can jump; the Stopwatch-based monotonic clock can't).
            var ttlMs = Math.Max(0.0, (expiresAtUnix - DateTimeOffset.UtcNow.ToUnixTimeSeconds()) * 1000.0);

            var grant = new CachedGrant(
                Token: Field(root, "token", "data-plane credential").GetString()!,
                DataPlaneUrl: Field(root, "qdrant_url", "data-plane URL").GetString()!.TrimEnd('/'),
                RealCollectionName: ResolveRealCollectionName(root),
                ExpiresAtElapsedMs: _clock.Elapsed.TotalMilliseconds + ttlMs,
                RecordedEmbedModel: root.TryGetProperty("embed_model", out var em) ? em.GetString() : null);

            _cache[key] = grant;
            return grant;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Prefers "physical_collection" (the current field), falling back to the older
    /// "collection_name" for a response that only carries that -- the real backend sends both
    /// today, so this fallback is defensive, not required.
    /// </summary>
    private static string ResolveRealCollectionName(JsonElement root)
    {
        if (root.TryGetProperty("physical_collection", out var physical))
        {
            return physical.GetString()!;
        }
        return Field(root, "collection_name", "tenant-namespaced collection name").GetString()!;
    }

    /// <summary>
    /// Reads one field from the token-exchange response, raising a clear, non-leaking error
    /// instead of letting a missing field surface a raw property-not-found failure -- the raw
    /// field name (which names the underlying vector-store tech) never reaches a caller-visible
    /// message.
    /// </summary>
    private static JsonElement Field(JsonElement body, string key, string description)
    {
        if (!body.TryGetProperty(key, out var value))
        {
            throw new LiviateException(
                $"Vector store token exchange returned an unexpected response (missing {description}).");
        }
        return value;
    }

    /// <summary>
    /// Returns the embed model recorded against this collection at ingest time, or null if none
    /// is recorded (true for any collection that predates this feature). Reuses the same grant
    /// cache SearchAsync does, so calling this before a search against the same collection costs
    /// no extra network round trip.
    /// </summary>
    public async Task<string?> GetRecordedEmbedModelAsync(string collection)
    {
        var grant = await GrantForAsync(collection, "r");
        return grant.RecordedEmbedModel;
    }

    public async Task<List<SearchHit>> SearchAsync(string collection, IReadOnlyList<float> vector, int topK, object? filter)
    {
        var grant = await GrantForAsync(collection, "r");
        var body = new Dictionary<string, object?> { ["query"] = vector, ["limit"] = topK, ["with_payload"] = true };
        if (filter is not null)
        {
            body["filter"] = filter;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/collections/{grant.RealCollectionName}/points/query")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", grant.Token);

        using var response = await DataHttpFor(grant.DataPlaneUrl).SendAsync(request);
        await HttpErrors.RaiseForStatusAsync(response);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var points = doc.RootElement.GetProperty("result").GetProperty("points");

        var hits = new List<SearchHit>();
        foreach (var p in points.EnumerateArray())
        {
            var id = p.TryGetProperty("id", out var idEl) ? idEl.ToString() : "";
            var score = p.GetProperty("score").GetDouble();
            // .Clone() detaches the element from the parent JsonDocument's pooled buffer --
            // without it, this JsonElement throws ObjectDisposedException as soon as the
            // `using var doc` above goes out of scope, since SearchHit escapes this method.
            var payload = p.TryGetProperty("payload", out var payloadEl) ? payloadEl.Clone() : (JsonElement?)null;
            hits.Add(new SearchHit(id, score, payload));
        }
        return hits;
    }

    public async Task UpsertAsync(string collection, IReadOnlyList<VectorStorePoint> points, string? embedModel = null)
    {
        var vectorSize = points.Count > 0 ? points[0].Vector.Count : (int?)null;
        var grant = await GrantForAsync(collection, "rw", vectorSize, embedModel);

        var body = new
        {
            points = points.Select(p => new { id = p.Id, vector = p.Vector, payload = p.Payload }),
        };

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/collections/{grant.RealCollectionName}/points")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", grant.Token);

        using var response = await DataHttpFor(grant.DataPlaneUrl).SendAsync(request);
        await HttpErrors.RaiseForStatusAsync(response);
    }

    /// <summary>Deletes points by id or by metadata filter -- exactly one of the two must be given.</summary>
    public async Task DeleteAsync(string collection, IReadOnlyList<string>? ids = null, object? filter = null)
    {
        if ((ids is null) == (filter is null))
        {
            throw new ArgumentException("DeleteAsync requires exactly one of ids or filter, not both/neither.");
        }

        var grant = await GrantForAsync(collection, "rw");
        object body = ids is not null ? new { points = ids } : new { filter };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/collections/{grant.RealCollectionName}/points/delete")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", grant.Token);

        using var response = await DataHttpFor(grant.DataPlaneUrl).SendAsync(request);
        await HttpErrors.RaiseForStatusAsync(response);
    }

    private sealed record CachedGrant(
        string Token, string DataPlaneUrl, string RealCollectionName, double ExpiresAtElapsedMs,
        string? RecordedEmbedModel = null)
    {
        public bool Usable(Stopwatch clock) => clock.Elapsed.TotalMilliseconds < ExpiresAtElapsedMs - RefreshMarginMs;
    }
}
