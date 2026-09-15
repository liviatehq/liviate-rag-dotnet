namespace Liviate.Rag.Exceptions;

/// <summary>
/// <c>Ingest(..., wait: true)</c> exceeded its timeout. Retry with <c>wait: false</c> and await
/// the returned <see cref="Liviate.Rag.Models.IngestJob"/> instead.
/// </summary>
public class IngestTimeoutException : LiviateException
{
    public IngestTimeoutException()
        : base("Ingest() timed out while waiting for completion. Retry with wait=false and " +
               "await the returned IngestJob instead.")
    {
    }
}
