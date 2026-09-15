namespace Liviate.Rag.Models;

/// <summary>
/// Returned by <c>RagClient.IngestAsync(..., wait: false)</c> instead of blocking. The work is
/// already running as a background <see cref="Task"/> (there is no server-side ingest job to
/// poll -- see <see cref="Liviate.Rag.Ingest.IngestPipeline"/>); this just lets the caller
/// observe or await it later.
/// </summary>
public sealed class IngestJob
{
    private readonly Task<IngestResult> _task;

    internal IngestJob(string jobId, string collection, Task<IngestResult> task)
    {
        JobId = jobId;
        Collection = collection;
        _task = task;
    }

    public string JobId { get; }

    public string Collection { get; }

    public string Status => _task.Status switch
    {
        TaskStatus.RanToCompletion => "done",
        TaskStatus.Faulted or TaskStatus.Canceled => "failed",
        _ => "running",
    };

    /// <summary>Awaits completion, throwing if the ingest failed.</summary>
    public Task<IngestResult> ResultAsync() => _task;

    /// <summary>Alias for <see cref="ResultAsync"/> when the result value isn't needed.</summary>
    public Task WaitAsync() => _task;
}
