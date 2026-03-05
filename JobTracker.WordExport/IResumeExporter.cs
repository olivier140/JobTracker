namespace JobTracker.WordExport;

using JobTracker.Core;

/// <summary>
/// Exports a tailored resume from a <see cref="JobMatch"/> to a Word (.docx) document.
/// </summary>
public interface IResumeExporter
{
    /// <summary>
    /// Exports the tailored resume in <paramref name="match"/> to a .docx file, embedding
    /// job metadata (title, score, top matches, gaps) at the end of the document.
    /// </summary>
    Task<ExportResult> ExportAsync(JobMatch match, ScrapedJob job, CancellationToken ct = default);

    /// <summary>
    /// Looks up the match by <paramref name="jobMatchId"/>, then delegates to
    /// <see cref="ExportAsync(JobMatch,ScrapedJob,CancellationToken)"/>.
    /// </summary>
    Task<ExportResult> ExportAsync(int jobMatchId, CancellationToken ct = default);
}
