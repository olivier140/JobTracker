// JobTracker.Core/MicrosoftJobsScraper.cs
using JobTracker.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;

/// <summary>
/// Provides functionality to scrape job postings from the Microsoft Careers website and persist new jobs to a database
/// asynchronously.
/// </summary>
/// <remarks>This class is designed to be used as part of a job tracking or aggregation system. It supports
/// progress reporting via the OnProgress event and is intended for use in applications that require automated retrieval
/// and storage of Microsoft job postings. The class is not thread-safe; create a separate instance for each concurrent
/// scraping operation.</remarks>
public class MicrosoftJobsScraper : IJobScraper
{
    private readonly IDbContextFactory<JobTrackerDbContext> _dbFactory;
    private readonly ILogger<MicrosoftJobsScraper> _logger;
    private readonly HttpClient _http;

    private const string BaseUrl = "https://apply.careers.microsoft.com";
    private const string SearchPath = "/api/pcsx/search";
    private const string DetailPath = "/api/pcsx/position_details";
    private const int PageSize = 10; // Default page size returned by PCSX API

    // Progress event for UI updates
    public event Action<string>? OnProgress;

    /// <summary>
    /// Initializes a new instance of the MicrosoftJobsScraper class with the specified database context factory and logger.
    /// </summary>
    /// <param name="dbFactory">The factory used to create instances of the JobTrackerDbContext for database operations. Cannot be null.</param>
    /// <param name="logger">The logger used to record diagnostic and operational information for the scraper. Cannot be null.</param>
    public MicrosoftJobsScraper(IDbContextFactory<JobTrackerDbContext> dbFactory, ILogger<MicrosoftJobsScraper> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;

        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true
        };
        _http = new HttpClient(handler);
        _http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    /// <summary>
    /// Scrapes job postings matching the specified query and location, and persists any new jobs to the database
    /// asynchronously.
    /// </summary>
    /// <remarks>If a job posting has already been persisted, it will not be added again. The method fetches
    /// results in pages and stops early if no more results are available or if cancellation is requested. Progress
    /// updates may be reported via the OnProgress event or delegate, if subscribed.</remarks>
    /// <param name="query">The search term used to filter job postings. Cannot be null or empty.</param>
    /// <param name="location">The location to filter job postings by. Cannot be null or empty.</param>
    /// <param name="maxPages">The maximum number of result pages to fetch. Must be greater than zero.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A list of newly scraped job postings that were persisted to the database. The list is empty if no new jobs were
    /// found.</returns>
    public async Task<List<ScrapedJob>> ScrapeAndPersistAsync(string query, string location, int maxPages, CancellationToken ct = default)
    {
        var newJobs = new List<ScrapedJob>();

        // Initialize session by visiting the careers page (sets cookies needed for API calls)
        await InitializeSessionAsync(ct);

        for (int page = 0; page < maxPages && !ct.IsCancellationRequested; page++)
        {
            int start = page * PageSize;

            var searchUrl = $"{BaseUrl}{SearchPath}?domain=microsoft.com" +
                            $"&query={Uri.EscapeDataString(query)}" +
                            $"&location={Uri.EscapeDataString(location)}" +
                            $"&start={start}&";

            OnProgress?.Invoke($"Fetching page {page + 1} (start={start})...");

            var response = await _http.GetAsync(searchUrl, ct);
            response.EnsureSuccessStatusCode();

            var apiResponse = await response.Content.ReadFromJsonAsync<PcsxApiResponse<PcsxSearchData>>(cancellationToken: ct);
            var positions = apiResponse?.Data?.Positions;
            if (positions == null || positions.Count == 0)
            {
                OnProgress?.Invoke($"No more positions found at page {page + 1}.");
                break;
            }

            var totalCount = apiResponse?.Data?.Count ?? 0;
            _logger.LogInformation("Search returned {Count} positions (total: {Total})", positions.Count, totalCount);

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Check each position against the database and fetch full description if it's new
            foreach (var pos in positions)
            {
                if (ct.IsCancellationRequested) break;

                var jobId = pos.DisplayJobId ?? pos.Id.ToString();
                if (await db.ScrapedJobs.AnyAsync(j => j.JobId == jobId, ct)) continue;

                // Fetch full description
                var description = await GetFullDescriptionAsync(pos.Id, ct);

                var primaryLocation = pos.Locations?.FirstOrDefault() ?? "";
                var postedDate = pos.PostedTs > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(pos.PostedTs).UtcDateTime
                    : (DateTime?)null;

                // Create new ScrapedJob entity
                var entity = new ScrapedJob
                {
                    JobId = jobId,
                    Title = pos.Name,
                    Location = primaryLocation.Length > 200 ? primaryLocation[..200] : primaryLocation,
                    DescriptionFull = description,
                    Url = $"{BaseUrl}{pos.PositionUrl ?? $"/careers/job/{pos.Id}"}",
                    PostedDate = postedDate,
                    ScrapedAt = DateTime.UtcNow
                };
                db.ScrapedJobs.Add(entity);
                newJobs.Add(entity);
            }

            await db.SaveChangesAsync(ct);
            OnProgress?.Invoke($"Page {page + 1}: {newJobs.Count} new jobs found so far.");

            // Stop if we've fetched all results
            if (start + positions.Count >= totalCount) break;

            // Polite delay between pages
            await Task.Delay(1000, ct);
        }

        return newJobs;
    }

    /// <summary>
    /// Initializes the session by performing a preliminary request to the careers page endpoint.
    /// </summary>
    /// <remarks>This method attempts to establish a session, which may be required for subsequent API calls
    /// that depend on cookies or session state. If the initialization fails, API calls may still function, but some
    /// features could be unavailable.</remarks>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous initialization operation.</returns>
    private async Task InitializeSessionAsync(CancellationToken ct)
    {
        try
        {
            var initResponse = await _http.GetAsync($"{BaseUrl}/careers", ct);
            initResponse.EnsureSuccessStatusCode();
            _logger.LogDebug("Session initialized with careers page");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize session, API calls may still work without cookies");
        }
    }

    /// <summary>
    /// Asynchronously retrieves the full job description for the specified position.
    /// </summary>
    /// <param name="positionId">The unique identifier of the position for which to retrieve the job description.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the full job description as a
    /// string, or null if the description could not be retrieved.</returns>
    private async Task<string?> GetFullDescriptionAsync(long positionId, CancellationToken ct)
    {
        try
        {
            var detailUrl = $"{BaseUrl}{DetailPath}?position_id={positionId}&domain=microsoft.com&hl=en";
            var res = await _http.GetAsync(detailUrl, ct);
            if (!res.IsSuccessStatusCode) return null;

            var apiResponse = await res.Content.ReadFromJsonAsync<PcsxApiResponse<PcsxPositionDetail>>(cancellationToken: ct);
            return apiResponse?.Data?.JobDescription;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch description for position {PositionId}", positionId);
            return null;
        }
    }
}
