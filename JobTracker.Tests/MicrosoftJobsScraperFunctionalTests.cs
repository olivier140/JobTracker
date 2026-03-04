using JobTracker.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System.Net;

namespace JobTracker.Tests;

[TestFixture]
[Category("Functional")]
public class MicrosoftJobsScraperFunctionalTests
{
    private const string SearchApiBase = "https://apply.careers.microsoft.com/api/pcsx/search";

    private static (IDbContextFactory<JobTrackerDbContext> factory, MicrosoftJobsScraper scraper) CreateScraper(string dbName)
    {
        var options = new DbContextOptionsBuilder<JobTrackerDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var factory = new TestDbContextFactory(options);
        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<MicrosoftJobsScraper>();
        return (factory, new MicrosoftJobsScraper(factory, logger));
    }

    private static async Task SkipIfApiUnavailable()
    {
        var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true };
        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        http.DefaultRequestHeaders.Add("Accept", "application/json");
        try
        {
            var response = await http.GetAsync(
                $"{SearchApiBase}?domain=microsoft.com&query=test&location=&start=0");
            if (!response.IsSuccessStatusCode)
                Assert.Ignore($"Microsoft Careers PCSX API unavailable (HTTP {(int)response.StatusCode}).");
        }
        catch (HttpRequestException ex)
        {
            Assert.Ignore($"Microsoft Careers API unreachable: {ex.Message}");
        }
    }

    [Test]
    public async Task ScrapeAndPersist_ReturnsJobsFromMicrosoftCareers()
    {
        await SkipIfApiUnavailable();
        var (factory, scraper) = CreateScraper(nameof(ScrapeAndPersist_ReturnsJobsFromMicrosoftCareers));

        var jobs = await scraper.ScrapeAndPersistAsync("software engineer", "United States", maxPages: 1);

        Assert.That(jobs, Is.Not.Empty);
        foreach (var job in jobs)
        {
            Assert.That(job.JobId, Is.Not.Null.And.Not.Empty);
            Assert.That(job.Title, Is.Not.Null.And.Not.Empty);
            Assert.That(job.Url, Is.Not.Null.And.Not.Empty);
            Assert.That(job.Url, Does.Contain("careers.microsoft.com"));
        }
    }

    [Test]
    public async Task ScrapeAndPersist_PersistsJobsToDatabase()
    {
        await SkipIfApiUnavailable();
        var (factory, scraper) = CreateScraper(nameof(ScrapeAndPersist_PersistsJobsToDatabase));

        var jobs = await scraper.ScrapeAndPersistAsync("software engineer", "United States", maxPages: 1);

        await using var db = factory.CreateDbContext();
        var dbCount = await db.ScrapedJobs.CountAsync();
        Assert.That(dbCount, Is.EqualTo(jobs.Count));
    }

    [Test]
    public async Task ScrapeAndPersist_FetchesFullDescription()
    {
        await SkipIfApiUnavailable();
        var (factory, scraper) = CreateScraper(nameof(ScrapeAndPersist_FetchesFullDescription));

        var jobs = await scraper.ScrapeAndPersistAsync("software engineer", "United States", maxPages: 1);

        // At least some jobs should have full descriptions (the detail API call)
        var withDescription = jobs.Where(j => !string.IsNullOrEmpty(j.DescriptionFull)).ToList();
        Assert.That(withDescription, Is.Not.Empty);
        Assert.That(withDescription[0].DescriptionFull!.Length, Is.GreaterThan(50),
            "Full description should be substantial text");
    }

    [Test]
    public async Task ScrapeAndPersist_SkipsDuplicatesOnSecondRun()
    {
        await SkipIfApiUnavailable();
        var (factory, scraper) = CreateScraper(nameof(ScrapeAndPersist_SkipsDuplicatesOnSecondRun));

        var firstRun = await scraper.ScrapeAndPersistAsync("software engineer", "United States", maxPages: 1);
        Assert.That(firstRun, Is.Not.Empty);

        // Second run with same query should find duplicates and return fewer/no new jobs
        var secondRun = await scraper.ScrapeAndPersistAsync("software engineer", "United States", maxPages: 1);
        Assert.That(secondRun.Count, Is.LessThan(firstRun.Count),
            $"Second run ({secondRun.Count}) should return fewer new jobs than first run ({firstRun.Count})");
    }

    [Test]
    public async Task ScrapeAndPersist_FiresProgressEvent()
    {
        await SkipIfApiUnavailable();
        var (factory, scraper) = CreateScraper(nameof(ScrapeAndPersist_FiresProgressEvent));
        var progressMessages = new List<string>();
        scraper.OnProgress += msg => progressMessages.Add(msg);

        await scraper.ScrapeAndPersistAsync("software engineer", "United States", maxPages: 1);

        Assert.That(progressMessages, Is.Not.Empty);
    }

    [Test]
    public async Task ScrapeAndPersist_RespectsLocationFilter()
    {
        await SkipIfApiUnavailable();
        var (factory, scraper) = CreateScraper(nameof(ScrapeAndPersist_RespectsLocationFilter));

        var jobs = await scraper.ScrapeAndPersistAsync("software engineer", "United States", maxPages: 1);

        Assert.That(jobs, Is.Not.Empty);
        // Jobs should have location data populated
        var withLocation = jobs.Where(j => !string.IsNullOrEmpty(j.Location)).ToList();
        Assert.That(withLocation, Is.Not.Empty);
    }

    [Test]
    public async Task ScrapeAndPersist_SupportsCancellation()
    {
        var (factory, scraper) = CreateScraper(nameof(ScrapeAndPersist_SupportsCancellation));
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var jobs = await scraper.ScrapeAndPersistAsync("software engineer", "United States", maxPages: 5, cts.Token);

        Assert.That(jobs, Is.Empty);
    }

    private class TestDbContextFactory : IDbContextFactory<JobTrackerDbContext>
    {
        private readonly DbContextOptions<JobTrackerDbContext> _options;
        public TestDbContextFactory(DbContextOptions<JobTrackerDbContext> options) => _options = options;
        public JobTrackerDbContext CreateDbContext() => new(_options);
    }
}
