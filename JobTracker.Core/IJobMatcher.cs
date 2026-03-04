namespace JobTracker.Core;

/// <summary>
/// Defines methods for scoring job matches based on a resume and updating application statuses, with support for
/// progress notifications.
/// </summary>
/// <remarks>Implementations of this interface may perform asynchronous operations that process job data and
/// update application statuses. The <see cref="OnProgress"/> event can be used to receive progress updates during
/// long-running operations, such as scoring multiple jobs. All methods are asynchronous and support cancellation via a
/// <see cref="CancellationToken"/> parameter.</remarks>
public interface IJobMatcher
{
    event Action<string>? OnProgress;
    Task ScoreAllUnscoredAsync(string resume, int minScore, CancellationToken ct = default);
    Task<JobMatch?> ScoreAndPersistAsync(ScrapedJob job, string resume, int minScore, CancellationToken ct = default);
    Task UpdateStatusAsync(int appId, string newStatus, string? notes = null);
}
