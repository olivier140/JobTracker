using JobTracker.Core;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace JobTracker.Tests;

[TestFixture]
public class ScraperWorkerTests
{
    private Mock<IJobScraper> _scraper = null!;
    private Mock<IJobMatcher> _matcher = null!;
    private Mock<ILogger<ScraperWorker>> _logger = null!;
    private AppSettings _settings = null!;

    [SetUp]
    public void SetUp()
    {
        _scraper = new Mock<IJobScraper>();
        _matcher = new Mock<IJobMatcher>();
        _logger = new Mock<ILogger<ScraperWorker>>();
        _settings = new AppSettings
        {
            SearchQuery = "software engineer",
            SearchLocation = "United States",
            MaxPages = 2,
            MinScoreToApply = 7,
            Resume = "Test resume content",
            ScheduleHours = 24
        };
    }

    private ScraperWorker CreateWorker() => new(_logger.Object, _settings, _scraper.Object, _matcher.Object);

    [Test]
    public async Task ExecuteAsync_RunsPipelineOnce_BeforeCancellation()
    {
        _scraper.Setup(s => s.ScrapeAndPersistAsync(
                _settings.SearchQuery, _settings.SearchLocation, _settings.MaxPages, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScrapedJob>());

        var cts = new CancellationTokenSource();
        var worker = CreateWorker();

        // Cancel after scraper completes so we exit after one pipeline run
        _matcher.Setup(m => m.ScoreAllUnscoredAsync(
                _settings.Resume, _settings.MinScoreToApply, It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .Returns(Task.CompletedTask);

        await worker.StartAsync(cts.Token);
        // Give the background task a moment to complete
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);

        _scraper.Verify(s => s.ScrapeAndPersistAsync(
            _settings.SearchQuery, _settings.SearchLocation, _settings.MaxPages,
            It.IsAny<CancellationToken>()), Times.Once);

        _matcher.Verify(m => m.ScoreAllUnscoredAsync(
            _settings.Resume, _settings.MinScoreToApply,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_CallsScraperThenMatcher_InOrder()
    {
        var callOrder = new List<string>();

        _scraper.Setup(s => s.ScrapeAndPersistAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("scraper"))
            .ReturnsAsync(new List<ScrapedJob>());

        var cts = new CancellationTokenSource();
        _matcher.Setup(m => m.ScoreAllUnscoredAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback(() => { callOrder.Add("matcher"); cts.Cancel(); })
            .Returns(Task.CompletedTask);

        var worker = CreateWorker();
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);

        Assert.That(callOrder, Is.EqualTo(new[] { "scraper", "matcher" }));
    }

    [Test]
    public async Task ExecuteAsync_PassesSettingsToScraper()
    {
        string? capturedQuery = null;
        string? capturedLocation = null;
        int capturedPages = 0;

        _scraper.Setup(s => s.ScrapeAndPersistAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, int, CancellationToken>((q, l, p, _) =>
            {
                capturedQuery = q;
                capturedLocation = l;
                capturedPages = p;
            })
            .ReturnsAsync(new List<ScrapedJob>());

        var cts = new CancellationTokenSource();
        _matcher.Setup(m => m.ScoreAllUnscoredAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .Returns(Task.CompletedTask);

        var worker = CreateWorker();
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);

        Assert.That(capturedQuery, Is.EqualTo("software engineer"));
        Assert.That(capturedLocation, Is.EqualTo("United States"));
        Assert.That(capturedPages, Is.EqualTo(2));
    }

    [Test]
    public async Task ExecuteAsync_PassesResumeAndMinScoreToMatcher()
    {
        string? capturedResume = null;
        int capturedMinScore = 0;

        _scraper.Setup(s => s.ScrapeAndPersistAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScrapedJob>());

        var cts = new CancellationTokenSource();
        _matcher.Setup(m => m.ScoreAllUnscoredAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<string, int, CancellationToken>((r, ms, _) =>
            {
                capturedResume = r;
                capturedMinScore = ms;
                cts.Cancel();
            })
            .Returns(Task.CompletedTask);

        var worker = CreateWorker();
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);

        Assert.That(capturedResume, Is.EqualTo("Test resume content"));
        Assert.That(capturedMinScore, Is.EqualTo(7));
    }

    [Test]
    public async Task ExecuteAsync_ScraperThrows_DoesNotCrashService()
    {
        _scraper.Setup(s => s.ScrapeAndPersistAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        var cts = new CancellationTokenSource();
        var worker = CreateWorker();

        await worker.StartAsync(cts.Token);
        // Let the pipeline run and fail
        await Task.Delay(300);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        // Matcher should NOT be called when scraper throws
        _matcher.Verify(m => m.ScoreAllUnscoredAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_MatcherThrows_DoesNotCrashService()
    {
        _scraper.Setup(s => s.ScrapeAndPersistAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScrapedJob>());

        _matcher.Setup(m => m.ScoreAllUnscoredAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("API error"));

        var cts = new CancellationTokenSource();
        var worker = CreateWorker();

        await worker.StartAsync(cts.Token);
        await Task.Delay(300);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        // Service survived the exception - scraper was still called
        _scraper.Verify(s => s.ScrapeAndPersistAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Test]
    public async Task ExecuteAsync_WiresProgressEvents()
    {
        var worker = CreateWorker();

        // The constructor should have wired OnProgress events
        _scraper.VerifyAdd(s => s.OnProgress += It.IsAny<Action<string>>(), Times.Once);
        _matcher.VerifyAdd(m => m.OnProgress += It.IsAny<Action<string>>(), Times.Once);

        await worker.StopAsync(CancellationToken.None);
    }
}
