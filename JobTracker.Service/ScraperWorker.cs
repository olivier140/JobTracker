// JobTracker.Service/ScraperWorker.cs
using JobTracker.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Provides a background service that periodically scrapes job listings and matches them against user criteria.
/// </summary>
/// <remarks>The service runs on a scheduled interval defined by application settings. It logs progress and
/// errors, and coordinates scraping and matching operations using injected dependencies. This class is intended to be
/// hosted as part of an ASP.NET Core or .NET Worker Service application.</remarks>
public class ScraperWorker : BackgroundService
{
    private readonly ILogger<ScraperWorker> _logger;
    private readonly AppSettings _settings;
    private readonly IJobScraper _scraper;
    private readonly IJobMatcher _matcher;

    /// <summary>
    /// Initializes a new instance of the ScraperWorker class with the specified logger, application settings, job
    /// scraper, and job matcher.
    /// </summary>
    /// <param name="logger">The logger used to record informational and error messages during the scraping and matching process. Cannot be
    /// null.</param>
    /// <param name="settings">The application settings that configure the behavior of the scraper worker. Cannot be null.</param>
    /// <param name="scraper">The job scraper responsible for retrieving job data from external sources. Cannot be null.</param>
    /// <param name="matcher">The job matcher used to evaluate and match jobs according to defined criteria. Cannot be null.</param>
    public ScraperWorker(ILogger<ScraperWorker> logger, AppSettings settings, IJobScraper scraper, IJobMatcher matcher)
    {
        _logger = logger;
        _settings = settings;
        _scraper = scraper;
        _matcher = matcher;

        _scraper.OnProgress += msg => _logger.LogInformation("[Scraper] {Msg}", msg);
        _matcher.OnProgress += msg => _logger.LogInformation("[Matcher] {Msg}", msg);
    }

    /// <summary>
    /// Executes the background job processing loop, running the pipeline immediately and then at scheduled intervals
    /// until cancellation is requested.
    /// </summary>
    /// <remarks>This method is intended to be run by the hosting infrastructure and should not be called
    /// directly. The pipeline is executed immediately on service start and then repeatedly at intervals specified by
    /// the schedule settings. The loop continues until the provided cancellation token is signaled.</remarks>
    /// <param name="ct">A cancellation token that can be used to request termination of the background operation.</param>
    /// <returns>A task that represents the asynchronous execution of the job processing loop.</returns>
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("JobTracker Service started.");

        // Run immediately on start, then on schedule
        while (!ct.IsCancellationRequested)
        {
            await RunPipelineAsync(ct);

            var nextRun = TimeSpan.FromHours(_settings.ScheduleHours);
            _logger.LogInformation("Next run in {H} hours at {Time}.",
                _settings.ScheduleHours,
                DateTime.Now.Add(nextRun).ToString("g"));

            await Task.Delay(nextRun, ct);
        }
    }

    /// <summary>
    /// Executes the job scraping and matching pipeline asynchronously, handling logging and cancellation.
    /// </summary>
    /// <remarks>The pipeline performs job scraping based on the configured search settings and then scores unscored
    /// jobs using the current resume. Logging is performed at key stages of the pipeline. If the operation is canceled via
    /// the provided token, the pipeline stops without logging an error.</remarks>
    /// <param name="ct">A cancellation token that can be used to cancel the pipeline operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task RunPipelineAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("=== Pipeline started at {Time} ===", DateTime.Now);

            _logger.LogInformation("Scraping: '{Q}' in '{L}' ({P} pages)...",
                _settings.SearchQuery, _settings.SearchLocation, _settings.MaxPages);

            var newJobs = await _scraper.ScrapeAndPersistAsync(
                _settings.SearchQuery,
                _settings.SearchLocation,
                _settings.MaxPages, ct);

            _logger.LogInformation("{Count} new jobs scraped.", newJobs.Count);

            await _matcher.ScoreAllUnscoredAsync(_settings.GetResume(), _settings.MinScoreToApply, ct);

            _logger.LogInformation("=== Pipeline complete ===");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "JobTracker Pipeline failed.");
        }
    }
}