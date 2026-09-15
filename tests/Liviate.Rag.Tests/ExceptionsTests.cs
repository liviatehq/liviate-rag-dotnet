using Liviate.Rag.Exceptions;
using Xunit;

namespace Liviate.Rag.Tests;

public class ExceptionsTests
{
    [Theory]
    [InlineData(typeof(UnsupportedFileTypeException))]
    [InlineData(typeof(IngestTimeoutException))]
    [InlineData(typeof(PartialIngestException))]
    [InlineData(typeof(RateLimitException))]
    public void AllCustomExceptionsInheritLiviateException(Type exceptionType)
    {
        Assert.True(typeof(LiviateException).IsAssignableFrom(exceptionType));
    }

    [Fact]
    public void LiviateExceptionIsNotAnArgumentException()
    {
        // Ingest()'s classification failures throw the builtin ArgumentException directly, not
        // a LiviateException subclass -- confirm the hierarchies stay separate so a broad
        // `catch (LiviateException)` doesn't silently swallow classification bugs.
        Assert.False(typeof(ArgumentException).IsAssignableFrom(typeof(LiviateException)));
    }

    [Fact]
    public void RateLimitExceptionCarriesRetryAfter()
    {
        var exc = new RateLimitException("rate limited", 12.5);
        Assert.Equal(12.5, exc.RetryAfter);
    }

    [Fact]
    public void IngestTimeoutDefaultMessageMentionsWaitFalse()
    {
        var exc = new IngestTimeoutException();
        Assert.Contains("wait=false", exc.Message);
    }
}
