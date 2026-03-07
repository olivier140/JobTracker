namespace JobTracker.Core.Commands;

/// <summary>
/// Command sent after a tailored resume has been successfully exported to a Word document.
/// Carries all relevant job, match, and export information for downstream processing.
/// </summary>
/// <remarks>
/// This is a plain POCO with no NServiceBus dependency. Endpoints identify it as a
/// command via unobtrusive conventions keyed on the <c>JobTracker.Core.Commands</c> namespace.
/// </remarks>
public class ResumeExportedCommand
{
    // ── ScrapedJob identity ──────────────────────────────────────────
    public int ScrapedJobId { get; set; }
    public string JobId { get; set; } = "";
    public string? JobTitle { get; set; }
    public string? JobLocation { get; set; }
    public string? JobUrl { get; set; }

    // ── JobMatch identity ────────────────────────────────────────────
    public int JobMatchId { get; set; }
    public int Score { get; set; }
    public bool RecommendApply { get; set; }

    // ── Export details ───────────────────────────────────────────────
    public string ExportedFilePath { get; set; } = "";
    public DateTime ExportedAtUtc { get; set; }
}
