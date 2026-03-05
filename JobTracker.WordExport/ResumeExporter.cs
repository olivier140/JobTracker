namespace JobTracker.WordExport;

using JobTracker.Core;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Default implementation of <see cref="IResumeExporter"/>. Writes tailored resumes as
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

    /// <inheritdoc/>
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

        var filePath = Path.Combine(outputDir, BuildFileName(job));

        try
        {
            await using var stream = new FileStream(
                filePath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 4096, useAsync: true);

            ResumeDocumentBuilder.Build(stream, match, job);
            await stream.FlushAsync(ct);

            return new ExportResult(true, filePath, null);
        }
        catch (Exception ex)
        {
            return new ExportResult(false, null, $"Failed to write document: {ex.Message}");
        }
    }

    /// <inheritdoc/>
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
    /// Determines the directory path to use for exporting files based on the current settings.
    /// </summary>
    /// <remarks>The default directory is located at 'JobTracker\Resumes' within the user's Documents folder
    /// if no export path is configured.</remarks>
    /// <returns>A string containing the output directory path. If an export path is specified in the settings, that path is
    /// returned; otherwise, a default directory under the user's Documents folder is used.</returns>
    private string ResolveOutputDirectory() =>
        !string.IsNullOrWhiteSpace(_settings.ExportPath)
            ? _settings.ExportPath
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "JobTracker",
                "Resumes");

    internal static string BuildFileName(ScrapedJob job)
    {
        var safeTitle = SanitizeForFileName(job.Title ?? "Unknown");
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        return $"Resume_{safeTitle}_{job.JobId}_{date}.docx";
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
