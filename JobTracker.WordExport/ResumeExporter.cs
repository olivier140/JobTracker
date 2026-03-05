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

    // -----------------------------------------------------------------------
    // IResumeExporter
    // -----------------------------------------------------------------------

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

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

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

    internal static string SanitizeForFileName(string input)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = string.Concat(input.Select(c => invalid.Contains(c) ? '_' : c))
                              .Replace(' ', '_');
        return sanitized.Length > 50 ? sanitized[..50] : sanitized;
    }
}
