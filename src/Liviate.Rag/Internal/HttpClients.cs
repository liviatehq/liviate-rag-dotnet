using System.Net.Http.Headers;

namespace Liviate.Rag.Internal;

internal static class HttpClients
{
    /// <summary>
    /// The OpenAI-compatible gateway client for embed/rerank/generate. Bearer-auth with the
    /// caller's api key directly -- unlike the vector store, nothing here is exchanged for a
    /// different credential.
    /// </summary>
    public static HttpClient BuildGatewayClient(string apiKey, string baseUrl, TimeSpan timeout)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = timeout,
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }
}
