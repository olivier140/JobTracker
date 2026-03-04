// JobTracker.Core/ClaudeJobMatcher.cs
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using JobTracker.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

/// <summary>
/// Use Claude to score and match scraped jobs against the candidate's resume. This class implements the IJobMatcher
/// </summary>
public class ClaudeJobMatcher : IJobMatcher
{
    // Dependencies are injected via the constructor. The API key is required to authenticate with the Claude service, while
    private readonly IDbContextFactory<JobTrackerDbContext> _dbFactory;
    private readonly ILogger<ClaudeJobMatcher> _logger;
    // The API key is stored as a private readonly field and is used for all interactions with the Anthropic SDK.
    private readonly string _apiKey;
    // Specify the Claude model to use for scoring and tailoring. Adjust as needed based on your requirements and API access.
    private const string Model = "claude-sonnet-4-6";

    // Event to report progress updates during scoring operations. Subscribers can listen to this event to receive real-time
    public event Action<string>? OnProgress;

    /// <summary>
    /// Initializes a new instance of the ClaudeJobMatcher class with the specified API key, database context factory,
    /// and logger.
    /// </summary>
    /// <param name="apiKey">The API key used to authenticate requests to the Claude service. Cannot be null or empty.</param>
    /// <param name="dbFactory">A factory for creating instances of JobTrackerDbContext used to access job tracking data. Cannot be null.</param>
    /// <param name="logger">The logger used to record diagnostic and operational information for the ClaudeJobMatcher. Cannot be null.</param>
    public ClaudeJobMatcher(string apiKey, IDbContextFactory<JobTrackerDbContext> dbFactory, ILogger<ClaudeJobMatcher> logger)
    {
        _apiKey = apiKey;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Scores all jobs that have not yet been scored using the specified resume and minimum score threshold.
    /// </summary>
    /// <remarks>This method processes all jobs that have not been scored and applies the scoring logic
    /// asynchronously. Progress updates may be reported during execution. If the operation is cancelled via the
    /// provided cancellation token, processing will stop as soon as possible.</remarks>
    /// <param name="resume">The resume text to use when evaluating and scoring unscored jobs. Cannot be null.</param>
    /// <param name="minScore">The minimum score a job must achieve to be considered a match.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task ScoreAllUnscoredAsync(string resume, int minScore, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var unscored = await db.ScrapedJobs
            .Where(j => j.Match == null)
            .ToListAsync(ct);

        OnProgress?.Invoke($"Scoring {unscored.Count} unscored jobs...");

        foreach (var job in unscored)
        {
            if (ct.IsCancellationRequested) break;
            await ScoreAndPersistAsync(job, resume, minScore, ct);
            await Task.Delay(400, ct);
        }
    }

    /// <summary>
    /// Scores a job match based on the provided resume and job details, persists the result, and optionally creates an
    /// application record if the score meets the specified minimum.
    /// </summary>
    /// <remarks>If a match for the specified job already exists, the method returns null and does not perform
    /// scoring or persistence. If the score meets or exceeds the specified minimum, an application record is also
    /// created and persisted. The method is asynchronous and may perform network and database operations.</remarks>
    /// <param name="job">The job to be evaluated and matched against the provided resume. Must not be null.</param>
    /// <param name="resume">The candidate's resume text to be analyzed for job matching. Cannot be null or empty.</param>
    /// <param name="minScore">The minimum score required to create an application record. Must be between 1 and 10.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A JobMatch object containing the scoring results and related data if the job has not already been matched;
    /// otherwise, null.</returns>
    public async Task<JobMatch?> ScoreAndPersistAsync(ScrapedJob job, string resume, int minScore, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        if (await db.JobMatches.AnyAsync(m => m.ScrapedJobId == job.Id, ct))
            return null;

        var client = new AnthropicClient(_apiKey);

        // The prompt sent to Claude is designed to elicit a structured JSON response that includes a score, top matches, gaps,
        // and a recommendation on whether to apply. The prompt provides the resume and job details as context for the evaluation.
        var raw = await CallClaudeAsync(client, $"""
            You are a career advisor. Return ONLY valid JSON (no markdown) with:
            - score (int 1-10)
            - top_matches (string[3])
            - gaps (string[] up to 3)
            - apply (bool)

            Resume: {resume}
            Job: {job.Title} | {job.Location}
            Description: {job.DescriptionFull}
            """, 500, ct);

        var score = JsonSerializer.Deserialize<JobScore>(raw ?? "{}");
        if (score == null) return null;

        string? tailored = null;
        if (score.Score >= minScore)
        {
            // If the score meets the minimum threshold, a second prompt is sent to Claude to generate a tailored version of the resume
            // that is optimized for the specific job. The prompt instructs Claude to reorder bullet points, mirror keywords from the job description,
            // and rewrite the summary section to better align with the role.
            // The response is expected to be plain text containing the tailored resume.
            tailored = await CallClaudeAsync(client, $"""
                Tailor this resume for the job below.
                - Reorder bullets by relevance
                - Mirror JD keywords
                - Rewrite summary for this role
                Return plain text resume only.

                Resume: {resume}
                Job: {job.Title}
                Description: {job.DescriptionFull}
                """, 2000, ct);
        }

        // A new JobMatch record is created and populated with the scoring results, including the score, top matches, gaps, recommendation,
        // and tailored resume. The record is then added to the database context.
        // If the score meets or exceeds the minimum threshold, a new ApplicationRecord is also created and associated with the JobMatch.
        // Finally, all changes are saved to the database.
        var match = new JobMatch
        {
            ScrapedJobId = job.Id,
            Score = score.Score,
            TopMatchesJson = JsonSerializer.Serialize(score.TopMatches),
            GapsJson = JsonSerializer.Serialize(score.Gaps),
            RecommendApply = score.Apply,
            TailoredResume = tailored,
            EvaluatedAt = DateTime.UtcNow
        };
        db.JobMatches.Add(match);

        if (score.Score >= minScore)
        {
            // The application record is initialized with a status of "Pending" and a follow-up date set to two weeks from the current date.
            db.Applications.Add(new ApplicationRecord
            {
                JobMatch = match,
                Status = "Pending",
                FollowUpAt = DateTime.UtcNow.AddDays(14),
                Notes = $"Auto-created. Score: {score.Score}/10"
            });
        }

        await db.SaveChangesAsync(ct);
        OnProgress?.Invoke($"Scored: {job.Title} → {score.Score}/10");
        return match;
    }

    /// <summary>
    /// Asynchronously updates the status of an application and records the change as an event.
    /// </summary>
    /// <remarks>If the specified application does not exist, no action is taken. The method also updates the
    /// application's last updated timestamp and, if the new status is "Applied", sets the application date. An event is
    /// recorded for the status change.</remarks>
    /// <param name="appId">The unique identifier of the application to update.</param>
    /// <param name="newStatus">The new status value to assign to the application. Cannot be null.</param>
    /// <param name="notes">Optional notes to associate with the status change. If null, existing notes are not modified.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task UpdateStatusAsync(int appId, string newStatus, string? notes = null)
    {
        // The method begins by creating a new instance of the database context using the factory.
        // It then attempts to find the application record with the specified ID.
        await using var db = await _dbFactory.CreateDbContextAsync();
        var app = await db.Applications.FindAsync(appId);
        if (app == null) return;

        var prev = app.Status;
        app.Status = newStatus;
        app.LastUpdatedAt = DateTime.UtcNow;
        if (notes != null) app.Notes = notes;
        if (newStatus == "Applied") app.AppliedAt = DateTime.UtcNow;

        // An ApplicationEvent record is created to log the status change, including the previous and new status values,
        // any associated notes, and the timestamp of the event.
        db.ApplicationEvents.Add(new ApplicationEvent
        {
            ApplicationId = app.Id,
            EventType = "StatusChanged",
            Detail = $"{prev} → {newStatus}. {notes}",
            OccurredAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Sends a prompt to the Claude model using the specified client and returns the generated text response
    /// asynchronously.
    /// </summary>
    /// <param name="client">The AnthropicClient instance used to send the request to the Claude model. Cannot be null.</param>
    /// <param name="prompt">The user prompt to send to the Claude model. Cannot be null or empty.</param>
    /// <param name="maxTokens">The maximum number of tokens to generate in the response. Must be a positive integer.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the generated text response from
    /// Claude, or null if no text content is returned.</returns>
    private async Task<string?> CallClaudeAsync(AnthropicClient client, string prompt, int maxTokens, CancellationToken ct)
    {
        // The method constructs a MessageParameters object that includes the specified model, maximum tokens, and a single user message containing the prompt.
        var response = await client.Messages.GetClaudeMessageAsync(
            new MessageParameters
            {
                Model = Model,
                MaxTokens = maxTokens,
                Messages = new List<Message>
                {
                    new Message
                    {
                        Role = RoleType.User,
                        Content = new List<ContentBase>
                        {
                            new TextContent { Text = prompt }
                        }
                    }
                }
            }, ct);
        return response.Content.OfType<TextContent>().FirstOrDefault()?.Text;
    }
}