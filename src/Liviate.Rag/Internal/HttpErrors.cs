using Liviate.Rag.Exceptions;

namespace Liviate.Rag.Internal;

internal static class HttpErrors
{
    public static async Task RaiseForStatusAsync(HttpResponseMessage response)
    {
        if ((int)response.StatusCode == 429)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds;
            throw new RateLimitException(
                $"Rate limited by Liviate API ({response.RequestMessage?.RequestUri}).", retryAfter);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new LiviateException(
                $"Liviate API returned {(int)response.StatusCode} {response.ReasonPhrase} for " +
                $"{response.RequestMessage?.RequestUri}: {Truncate(body)}");
        }
    }

    private static string Truncate(string body) => body.Length > 500 ? body[..500] + "..." : body;
}
