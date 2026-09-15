namespace Liviate.Rag.Ingest;

public enum SourceKind
{
    Batch,
    File,
    Url,
    Stream,
    Text,
}

/// <summary>Override for <see cref="SourceClassifier.Classify"/>'s auto-detection.</summary>
public enum SourceType
{
    Auto,
    File,
    Url,
    Text,
}

public sealed record Classification(SourceKind Kind, object Value);

/// <summary>
/// Pure, I/O-free source classification for Ingest(). No network calls, no file reads beyond
/// an existence check. This isolation is what makes the dispatch logic unit-testable without
/// mocking the filesystem or network -- see SourceClassifierTests.
///
/// Detection order (SourceType.Auto):
/// 1. IEnumerable (not a string) -> batch
/// 2. existing local path (string or FileInfo) -> file
/// 3. http(s):// string -> url
/// 4. Stream -> stream
/// 5. anything else -> ArgumentException (raw strings are never silently treated as text;
///    SourceType.Text must be explicit)
/// </summary>
public static class SourceClassifier
{
    public static Classification Classify(object source, SourceType sourceType = SourceType.Auto)
    {
        switch (sourceType)
        {
            case SourceType.Text:
                if (source is not string textValue)
                {
                    throw new ArgumentException(
                        $"SourceType.Text requires source to be a string, got {source.GetType().Name}.");
                }
                return new Classification(SourceKind.Text, textValue);

            case SourceType.File:
                return new Classification(SourceKind.File, RequireExistingPath(source));

            case SourceType.Url:
                return new Classification(SourceKind.Url, RequireUrlString(source));

            case SourceType.Auto:
                return ClassifyAuto(source);

            default:
                throw new ArgumentException($"Unknown source type: {sourceType}");
        }
    }

    private static Classification ClassifyAuto(object source)
    {
        if (source is not string && source is System.Collections.IEnumerable enumerable)
        {
            var items = enumerable.Cast<object>().ToList();
            return new Classification(SourceKind.Batch, items);
        }

        if (source is FileInfo fileInfo)
        {
            if (!fileInfo.Exists)
            {
                throw new ArgumentException($"Source does not exist: {fileInfo.FullName}");
            }
            return new Classification(SourceKind.File, fileInfo);
        }

        if (source is string stringSource)
        {
            if (File.Exists(stringSource))
            {
                return new Classification(SourceKind.File, new FileInfo(stringSource));
            }

            if (stringSource.StartsWith("http://", StringComparison.Ordinal) ||
                stringSource.StartsWith("https://", StringComparison.Ordinal))
            {
                return new Classification(SourceKind.Url, stringSource);
            }

            throw new ArgumentException(
                "Could not classify string source as an existing file path or a URL. If this " +
                "is raw text content, pass SourceType.Text explicitly -- raw strings are never " +
                "treated as text content by default.");
        }

        if (source is Stream streamSource)
        {
            return new Classification(SourceKind.Stream, streamSource);
        }

        throw new ArgumentException(
            $"Could not classify source of type {source.GetType().Name}. Pass SourceType " +
            "explicitly (File, Url, or Text).");
    }

    private static FileInfo RequireExistingPath(object source)
    {
        var path = source switch
        {
            FileInfo fi => fi.FullName,
            string s => s,
            _ => throw new ArgumentException(
                $"SourceType.File requires a string path or FileInfo, got {source.GetType().Name}."),
        };

        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
        {
            throw new ArgumentException($"SourceType.File but path does not exist: {path}");
        }
        return fileInfo;
    }

    private static string RequireUrlString(object source)
    {
        if (source is string s && (s.StartsWith("http://", StringComparison.Ordinal) ||
                                    s.StartsWith("https://", StringComparison.Ordinal)))
        {
            return s;
        }
        throw new ArgumentException($"SourceType.Url requires an http(s):// string, got {source}");
    }
}
