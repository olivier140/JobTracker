namespace JobTracker.Core;

/// <summary>
/// Defines methods for job scraping, persisting and notification.
/// </summary>
public interface IJobScraper
{
    event Action<string>? OnProgress;
    Task<List<ScrapedJob>> ScrapeAndPersistAsync(string query, string location, int maxPages, CancellationToken ct = default);
}
