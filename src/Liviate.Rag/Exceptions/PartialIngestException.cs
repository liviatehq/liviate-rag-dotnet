namespace Liviate.Rag.Exceptions;

/// <summary>
/// Some but not all chunks from a single source failed to ingest.
///
/// NOT THROWN in v1: the project brief and API reference for the Python reference
/// implementation both lean towards warning-carrying behavior (the readable parts still
/// succeed) pending product confirmation -- see <see cref="Liviate.Rag.Models.IngestResult.Warnings"/>.
/// This class exists so the surface is in place once that's confirmed; nothing in this
/// codebase currently throws it.
/// </summary>
public class PartialIngestException : LiviateException
{
    public PartialIngestException(string message) : base(message) { }
}
