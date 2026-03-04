// JobTracker.Console/Program.cs
// Runs the same scrape-and-score pipeline as the Windows Service, but once and exits.
using JobTracker.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Read API key from environment
string? apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Error: Environment variable ANTHROPIC_API_KEY is not set.");
    return 1;
}

// Read resume location from environment
string? resume = Environment.GetEnvironmentVariable("JOBTRACKER_RESUME");
if (string.IsNullOrWhiteSpace(resume))
{
    Console.Error.WriteLine("Error: Environment variable JOBTRACKER_RESUME is not set.");
    return 1;
}

// Load configuration
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var settings = new AppSettings();
settings.AnthropicApiKey = apiKey;
settings.Resume = resume;
config.GetSection("AppSettings").Bind(settings);

// Build DI container
var services = new ServiceCollection();
services.AddLogging(log => log.AddConsole());
services.AddJobTrackerCore(settings);
await using var sp = services.BuildServiceProvider();

// Ensure database exists
await ServiceRegistration.EnsureDatabaseAsync(sp);

// Resolve services
var scraper = sp.GetRequiredService<IJobScraper>();
var matcher = sp.GetRequiredService<IJobMatcher>();

// Wire up progress events to console
scraper.OnProgress += msg => Console.WriteLine($"[Scraper] {msg}");
matcher.OnProgress += msg => Console.WriteLine($"[Matcher] {msg}");

// Run pipeline
Console.WriteLine($"=== JobTracker Pipeline started at {DateTime.Now} ===");
Console.WriteLine($"Scraping: '{settings.SearchQuery}' in '{settings.SearchLocation}' ({settings.MaxPages} pages)...");

try
{
    var newJobs = await scraper.ScrapeAndPersistAsync(
        settings.SearchQuery,
        settings.SearchLocation,
        settings.MaxPages);

    Console.WriteLine($"{newJobs.Count} new jobs scraped.");

    await matcher.ScoreAllUnscoredAsync(settings.GetResume(), settings.MinScoreToApply);

    Console.WriteLine($"=== Pipeline complete ===");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Pipeline failed: {ex.Message}");
    return 1;
}

return 0;
