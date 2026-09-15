namespace Liviate.Rag;

/// <summary>
/// Construction options for <see cref="RagClient"/>. API key resolution: explicit
/// <c>apiKey</c> argument, else the <c>LIVIATE_API_KEY</c> environment variable. No other
/// source (config files, etc.) is supported by design.
/// </summary>
public sealed class RagClientOptions
{
    private const string ApiKeyEnvVar = "LIVIATE_API_KEY";

    /// <summary>Root URL for the OpenAI-compatible gateway (embed/rerank/generate).</summary>
    public string BaseUrl { get; init; } = "https://liviate.com";

    /// <summary>
    /// Overrides where the vector store's token-exchange call goes. It lives on a different
    /// host than <see cref="BaseUrl"/> even in production (console vs. the main gateway), so
    /// pointing <see cref="BaseUrl"/> at a non-default environment does NOT redirect this
    /// automatically -- set this explicitly too in that case.
    /// </summary>
    public string ExchangeUrl { get; init; } = "https://console.liviate.com/api/tenancy/vectordb/exchange-token/";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    internal static string ResolveApiKey(string? apiKey)
    {
        if (!string.IsNullOrEmpty(apiKey))
        {
            return apiKey;
        }

        var envValue = Environment.GetEnvironmentVariable(ApiKeyEnvVar);
        if (!string.IsNullOrEmpty(envValue))
        {
            return envValue;
        }

        throw new ArgumentException(
            $"No API key provided. Pass apiKey explicitly or set the {ApiKeyEnvVar} environment variable.");
    }
}
