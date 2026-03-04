using JobTracker.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace JobTracker.Tests;

[TestFixture]
public class UpdateStatusTests
{
    private static IDbContextFactory<JobTrackerDbContext> CreateInMemoryFactory(string dbName)
    {
        var options = new DbContextOptionsBuilder<JobTrackerDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new TestDbContextFactory(options);
    }

    [Test]
    public async Task UpdateStatusAsync_ChangesStatusAndCreatesEvent()
    {
        var factory = CreateInMemoryFactory(nameof(UpdateStatusAsync_ChangesStatusAndCreatesEvent));
        var matcher = new ClaudeJobMatcher("fake-key", factory,
            Microsoft.Extensions.Logging.LoggerFactory.Create(_ => { }).CreateLogger<ClaudeJobMatcher>());

        // Seed an application
        await using (var db = await factory.CreateDbContextAsync())
        {
            var job = new ScrapedJob { JobId = "J1", Title = "SWE" };
            db.ScrapedJobs.Add(job);
            await db.SaveChangesAsync();

            var match = new JobMatch { ScrapedJobId = job.Id, Score = 8 };
            db.JobMatches.Add(match);
            await db.SaveChangesAsync();

            db.Applications.Add(new ApplicationRecord { JobMatchId = match.Id, Status = "Pending" });
            await db.SaveChangesAsync();
        }

        await matcher.UpdateStatusAsync(1, "Applied", "Submitted online");

        await using (var db = await factory.CreateDbContextAsync())
        {
            var app = await db.Applications.FirstAsync();
            Assert.That(app.Status, Is.EqualTo("Applied"));
            Assert.That(app.AppliedAt, Is.Not.Null);

            var evt = await db.ApplicationEvents.FirstAsync();
            Assert.That(evt.EventType, Is.EqualTo("StatusChanged"));
            Assert.That(evt.Detail, Does.Contain("Pending → Applied"));
        }
    }

    [Test]
    public async Task UpdateStatusAsync_SetsAppliedAt_OnlyWhenApplied()
    {
        var factory = CreateInMemoryFactory(nameof(UpdateStatusAsync_SetsAppliedAt_OnlyWhenApplied));
        var matcher = new ClaudeJobMatcher("fake-key", factory,
            Microsoft.Extensions.Logging.LoggerFactory.Create(_ => { }).CreateLogger<ClaudeJobMatcher>());

        await using (var db = await factory.CreateDbContextAsync())
        {
            var job = new ScrapedJob { JobId = "J2", Title = "PM" };
            db.ScrapedJobs.Add(job);
            await db.SaveChangesAsync();

            var match = new JobMatch { ScrapedJobId = job.Id, Score = 9 };
            db.JobMatches.Add(match);
            await db.SaveChangesAsync();

            db.Applications.Add(new ApplicationRecord { JobMatchId = match.Id, Status = "Pending" });
            await db.SaveChangesAsync();
        }

        await matcher.UpdateStatusAsync(1, "Interview");

        await using (var db = await factory.CreateDbContextAsync())
        {
            var app = await db.Applications.FirstAsync();
            Assert.That(app.Status, Is.EqualTo("Interview"));
            Assert.That(app.AppliedAt, Is.Null); // Not "Applied", so AppliedAt stays null
        }
    }

    [Test]
    public async Task UpdateStatusAsync_NonexistentApp_DoesNotThrow()
    {
        var factory = CreateInMemoryFactory(nameof(UpdateStatusAsync_NonexistentApp_DoesNotThrow));
        var matcher = new ClaudeJobMatcher("fake-key", factory,
            Microsoft.Extensions.Logging.LoggerFactory.Create(_ => { }).CreateLogger<ClaudeJobMatcher>());

        // Should not throw for missing ID
        await matcher.UpdateStatusAsync(999, "Applied");
    }

    private class TestDbContextFactory : IDbContextFactory<JobTrackerDbContext>
    {
        private readonly DbContextOptions<JobTrackerDbContext> _options;
        public TestDbContextFactory(DbContextOptions<JobTrackerDbContext> options) => _options = options;
        public JobTrackerDbContext CreateDbContext() => new(_options);
    }
}
