namespace Liviate.Rag.Exceptions;

/// <summary>Thrown on HTTP 429 responses from any Liviate backend.</summary>
public class RateLimitException : LiviateException
{
    public double? RetryAfter { get; }

    public RateLimitException(string message, double? retryAfter = null) : base(message)
    {
        RetryAfter = retryAfter;
    }
}
