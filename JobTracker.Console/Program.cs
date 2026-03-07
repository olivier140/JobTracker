// JobTracker.Console/Program.cs
// Runs the same scrape-and-score pipeline as the Windows Service, but once and exits.
// Pass --export to also write tailored resumes to Word documents after scoring.
using JobTracker.Core;
using JobTracker.Core.Commands;
using JobTracker.WordExport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NServiceBus;

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

bool exportWord = args.Contains("--export") || settings.AutoExportOnScore;

// Build DI container
var services = new ServiceCollection();
services.AddLogging(log => log.AddConsole());
services.AddJobTrackerCore(settings);
if (exportWord) services.AddWordExport();
await using var sp = services.BuildServiceProvider();

// Ensure database exists
await ServiceRegistration.EnsureDatabaseAsync(sp);

// Start NServiceBus send-only endpoint when exporting
IEndpointInstance? busEndpoint = null;
if (exportWord)
{
    await ServiceRegistration.EnsureNsbSchemaAsync(settings.ConnectionString);

    var endpointConfig = new EndpointConfiguration("JobTracker.Console");
    endpointConfig.SendOnly();

    var transport = new SqlServerTransport(settings.ConnectionString);
    transport.DefaultSchema = "nsb";
    var routing = endpointConfig.UseTransport(transport);
    routing.RouteToEndpoint(typeof(ResumeExportedCommand), "JobTracker.Messaging");

    endpointConfig.Conventions()
        .DefiningCommandsAs(type => type.Namespace == "JobTracker.Core.Commands");

    endpointConfig.EnableInstallers();

    busEndpoint = await Endpoint.Start(endpointConfig);
}

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

    if (exportWord)
    {
        Console.WriteLine("Exporting tailored resumes and cover letters to Word...");
        var exporter = sp.GetRequiredService<IResumeExporter>();
        var dbFactory = sp.GetRequiredService<IDbContextFactory<JobTrackerDbContext>>();

        await using var db = await dbFactory.CreateDbContextAsync();
        var matches = await db.JobMatches
            .Include(m => m.ScrapedJob)
            .Where(m => m.TailoredResume != null && m.Score >= settings.MinScoreToApply)
            .ToListAsync();

        int exported = 0, failed = 0;
        foreach (var match in matches)
        {
            var result = await exporter.ExportAsync(match, match.ScrapedJob!, CancellationToken.None);
            if (result.Success)
            {
                Console.WriteLine($"  Resume:      {result.FilePath}");
                if (result.CoverLetterFilePath != null)
                    Console.WriteLine($"  Cover Letter:{result.CoverLetterFilePath}");
                exported++;

                // Send export notification to the bus
                if (busEndpoint is not null)
                {
                    try
                    {
                        await busEndpoint.Send(new ResumeExportedCommand
                        {
                            ScrapedJobId = match.ScrapedJob!.Id,
                            JobId = match.ScrapedJob.JobId,
                            JobTitle = match.ScrapedJob.Title,
                            JobLocation = match.ScrapedJob.Location,
                            JobUrl = match.ScrapedJob.Url,
                            JobMatchId = match.Id,
                            Score = match.Score,
                            RecommendApply = match.RecommendApply,
                            ExportedFilePath = result.FilePath!,
                            CoverLetterFilePath = result.CoverLetterFilePath,
                            ExportedAtUtc = DateTime.UtcNow
                        });
                    }
                    catch (Exception busEx)
                    {
                        Console.Error.WriteLine($"  Bus notification failed (non-fatal): {busEx.Message}");
                    }
                }
            }
            else
            {
                Console.Error.WriteLine($"  Failed ({match.ScrapedJob?.Title}): {result.Error}");
                failed++;
            }
        }

        Console.WriteLine($"Export complete: {exported} succeeded, {failed} failed.");
    }

    Console.WriteLine($"=== Pipeline complete ===");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Pipeline failed: {ex.Message}");
    return 1;
}
finally
{
    if (busEndpoint is not null)
        await busEndpoint.Stop();
}

return 0;
