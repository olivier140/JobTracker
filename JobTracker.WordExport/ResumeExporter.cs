namespace JobTracker.WordExport;

using JobTracker.Core;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Default implementation of <see cref="IResumeExporter"/>. Writes tailored resumes and cover letters as
/// .docx files to <c>%USERPROFILE%\Documents\JobTracker\Resumes\</c> (or the path
/// configured in <c>AppSettings.ExportPath</c>).
/// </summary>
public sealed class ResumeExporter : IResumeExporter
{
    private readonly IDbContextFactory<JobTrackerDbContext> _dbFactory;
    private readonly AppSettings _settings;

    public ResumeExporter(IDbContextFactory<JobTrackerDbContext> dbFactory, AppSettings settings)
    {
        _dbFactory = dbFactory;
        _settings = settings;
    }

    #region Implements IResumeExporter

    /// <summary>
    /// Asynchronously exports a tailored resume and, if available, a cover letter for the specified job match to the
    /// file system.
    /// </summary>
    /// <remarks>If the tailored resume is not available, the export will not proceed and the result will
    /// indicate failure. If exporting the cover letter fails but the resume export succeeds, the result will indicate
    /// partial success and include an error message for the cover letter. The method creates the output directory if it
    /// does not exist.</remarks>
    /// <param name="match">The job match containing the tailored resume and optional cover letter to export. Must have a non-empty tailored
    /// resume.</param>
    /// <param name="job">The job posting information used to determine file naming and document content.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the export operation.</param>
    /// <returns>An ExportResult indicating whether the export succeeded, the path to the exported resume file if successful, and
    /// any error message or the path to the exported cover letter if applicable.</returns>
    public async Task<ExportResult> ExportAsync(JobMatch match, ScrapedJob job, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(match.TailoredResume))
            return new ExportResult(false, null, "No tailored resume available for this job.");

        var outputDir = ResolveOutputDirectory();

        try
        {
            Directory.CreateDirectory(outputDir);
        }
        catch (Exception ex)
        {
            return new ExportResult(false, null, $"Cannot create output directory '{outputDir}': {ex.Message}");
        }

        var filePath = Path.Combine(outputDir, BuildResumeFileName(job));

        try
        {
            await using var stream = new FileStream(
                filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 4096, useAsync: true);

            ResumeDocumentBuilder.Build(stream, match, job);
            await stream.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            return new ExportResult(false, null, $"Failed to write resume document: {ex.Message}");
        }

        // Export cover letter if available.
        string? coverLetterPath = null;
        if (!string.IsNullOrWhiteSpace(match.CoverLetter))
        {
            coverLetterPath = Path.Combine(outputDir, BuildCoverLetterFileName(job));
            try
            {
                await using var stream = new FileStream(
                    coverLetterPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                    bufferSize: 4096, useAsync: true);

                CoverLetterDocumentBuilder.Build(stream, match, job);
                await stream.FlushAsync(ct);
            }
            catch (Exception ex)
            {
                // Non-fatal: resume succeeded, report cover letter failure in Error.
                return new ExportResult(true, filePath, $"Cover letter export failed: {ex.Message}");
            }
        }

        return new ExportResult(true, filePath, null, coverLetterPath);
    }

    /// <summary>
    /// Exports the job match and its associated scraped job data asynchronously for the specified job match identifier.
    /// </summary>
    /// <param name="jobMatchId">The unique identifier of the job match to export.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the export operation.</param>
    /// <returns>A task that represents the asynchronous export operation. The task result contains an ExportResult indicating
    /// whether the export was successful, the exported data if successful, or an error message if the operation failed.</returns>
    public async Task<ExportResult> ExportAsync(int jobMatchId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var match = await db.JobMatches
            .Include(m => m.ScrapedJob)
            .FirstOrDefaultAsync(m => m.Id == jobMatchId, ct);

        if (match is null)
            return new ExportResult(false, null, $"Job match {jobMatchId} not found.");

        if (match.ScrapedJob is null)
            return new ExportResult(false, null, $"Scraped job for match {jobMatchId} not found.");

        return await ExportAsync(match, match.ScrapedJob, ct);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Determines the output directory path to use for exporting resumes based on application settings and environment
    /// variables.
    /// </summary>
    /// <remarks>The method prioritizes the export path specified in the application settings. If not set, it checks
    /// for the 'JOBTRACKER_TAILORED' environment variable. If neither is available, it defaults to a standard directory
    /// within the user's Documents folder. The returned path is never null or whitespace.</remarks>
    /// <returns>A string containing the resolved output directory path. The path is determined by the export path setting, the
    /// JOBTRACKER_TAILORED environment variable, or defaults to the user's Documents folder under 'JobTracker\Resumes'.</returns>
    private string ResolveOutputDirectory() =>
        !string.IsNullOrWhiteSpace(_settings.ExportPath)
            ? _settings.ExportPath
            : !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JOBTRACKER_TAILORED"))
                ? Environment.GetEnvironmentVariable("JOBTRACKER_TAILORED")!
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "JobTracker", "Resumes");

    /// <summary>
    /// Builds a file name for a resume document based on the specified job information.
    /// </summary>
    /// <param name="job">The job for which to generate the resume file name. Cannot be null.</param>
    /// <returns>A string containing the generated file name in the format 'Resume_{Title}_{JobId}_{Date}.docx', where the title
    /// is sanitized for file system compatibility and the date is in 'yyyy-MM-dd' format.</returns>
    internal static string BuildResumeFileName(ScrapedJob job)
    {
        var safeTitle = SanitizeForFileName(job.Title ?? "Unknown");
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        return $"Resume_{safeTitle}_{job.JobId}_{date}.docx";
    }

    /// <summary>
    /// Builds a file name for a cover letter document based on the specified job information.
    /// </summary>
    /// <remarks>The generated file name is intended to be safe for use on most file systems. If the job title
    /// is null, 'Unknown' is used as a placeholder.</remarks>
    /// <param name="job">The job for which to generate the cover letter file name. Must not be null and should contain a valid title and
    /// job ID.</param>
    /// <returns>A string representing the generated file name in the format 'CoverLetter_{Title}_{JobId}_{Date}.docx', where the
    /// title is sanitized for file system compatibility and the date is the current date in 'yyyy-MM-dd' format.</returns>
    internal static string BuildCoverLetterFileName(ScrapedJob job)
    {
        var safeTitle = SanitizeForFileName(job.Title ?? "Unknown");
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        return $"CoverLetter_{safeTitle}_{job.JobId}_{date}.docx";
    }

    /// <summary>
    /// Returns a sanitized version of the specified string that is safe to use as a file name.
    /// </summary>
    /// <remarks>This method replaces all characters that are invalid in file names, as defined by the
    /// operating system, with underscores ('_'). Spaces are also replaced with underscores. If the sanitized string
    /// exceeds 50 characters, it is truncated to 50 characters. The method does not guarantee uniqueness of the
    /// resulting file name.</remarks>
    /// <param name="input">The input string to sanitize for use as a file name. May contain invalid or unsafe file name characters.</param>
    /// <returns>A string that is safe to use as a file name. All invalid file name characters and spaces are replaced with
    /// underscores. The result is truncated to a maximum of 50 characters.</returns>
    internal static string SanitizeForFileName(string input)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = string.Concat(input.Select(c => invalid.Contains(c) ? '_' : c))
                              .Replace(' ', '_');
        return sanitized.Length > 50 ? sanitized[..50] : sanitized;
    }

    #endregion
}
