using Liviate.Rag.Ingest;
using Xunit;

namespace Liviate.Rag.Tests;

/// <summary>
/// Unit tests for the pure Ingest() source-classification dispatch logic. No network or real
/// file I/O beyond temp-file fixtures -- see SourceClassifier's class docs for why this logic
/// is kept pure and tested in isolation. Mirrors Python's test_ingest_detect.py.
/// </summary>
public class SourceClassifierTests
{
    [Fact]
    public void ListIsDetectedAsBatch()
    {
        var result = SourceClassifier.Classify(new List<string> { "a.pdf", "b.pdf" });
        Assert.Equal(SourceKind.Batch, result.Kind);
        Assert.Equal(new List<object> { "a.pdf", "b.pdf" }, result.Value);
    }

    [Fact]
    public void ArrayIsDetectedAsBatch()
    {
        var result = SourceClassifier.Classify(new[] { "a.pdf", "b.pdf" });
        Assert.Equal(SourceKind.Batch, result.Kind);
    }

    [Fact]
    public void ExistingLocalPathIsDetectedAsFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            var result = SourceClassifier.Classify(path);
            Assert.Equal(SourceKind.File, result.Kind);
            Assert.Equal(path, ((FileInfo)result.Value).FullName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UrlStringsAreDetected()
    {
        var result = SourceClassifier.Classify("https://example.com/page");
        Assert.Equal(SourceKind.Url, result.Kind);
        Assert.Equal("https://example.com/page", result.Value);

        var httpResult = SourceClassifier.Classify("http://example.com/page");
        Assert.Equal(SourceKind.Url, httpResult.Kind);
    }

    [Fact]
    public void StreamIsDetectedAsStream()
    {
        using var stream = new MemoryStream("content"u8.ToArray());
        var result = SourceClassifier.Classify(stream);
        Assert.Equal(SourceKind.Stream, result.Kind);
        Assert.Same(stream, result.Value);
    }

    [Fact]
    public void AmbiguousNonexistentStringThrows()
    {
        Assert.Throws<ArgumentException>(() => SourceClassifier.Classify("this/path/does/not/exist.txt"));
    }

    [Fact]
    public void RelativeNonexistentPathThrows()
    {
        Assert.Throws<ArgumentException>(() => SourceClassifier.Classify("relative/typo/path.md"));
    }

    [Fact]
    public void RawTextWithoutExplicitSourceTypeIsNotSilentlyAccepted()
    {
        Assert.Throws<ArgumentException>(() => SourceClassifier.Classify("just some raw text content, not a path or url"));
    }

    [Fact]
    public void RawTextWithExplicitSourceTypeIsAccepted()
    {
        var result = SourceClassifier.Classify("just some raw text", SourceType.Text);
        Assert.Equal(SourceKind.Text, result.Kind);
        Assert.Equal("just some raw text", result.Value);
    }

    [Fact]
    public void SourceTypeTextRejectsNonString()
    {
        Assert.Throws<ArgumentException>(() => SourceClassifier.Classify(12345, SourceType.Text));
    }

    [Fact]
    public void UnclassifiableObjectThrows()
    {
        Assert.Throws<ArgumentException>(() => SourceClassifier.Classify(new object()));
    }

    [Fact]
    public void ExplicitSourceTypeFileRequiresExistingPath()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Assert.Throws<ArgumentException>(() => SourceClassifier.Classify(missingPath, SourceType.File));
    }

    [Fact]
    public void ExplicitSourceTypeUrlRequiresHttpScheme()
    {
        Assert.Throws<ArgumentException>(() => SourceClassifier.Classify("not-a-url", SourceType.Url));
    }
}
