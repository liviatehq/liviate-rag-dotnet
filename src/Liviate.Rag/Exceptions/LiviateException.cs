namespace Liviate.Rag.Exceptions;

/// <summary>
/// Base class for all liviate-rag errors (excluding <see cref="ArgumentException"/>, which
/// <c>Ingest</c> throws directly when it can't classify a source -- see
/// <see cref="Liviate.Rag.Ingest.SourceClassifier"/>).
/// </summary>
public class LiviateException : Exception
{
    public LiviateException(string message) : base(message) { }

    public LiviateException(string message, Exception innerException) : base(message, innerException) { }
}
