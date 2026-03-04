// JobTracker.Core/JobTrackerDbContext.cs
using JobTracker.Core;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Represents the Entity Framework Core database context for the job tracking application, providing access to job,
/// match, application, and event data.
/// </summary>
/// <remarks>This context manages the application's primary data entities, including scraped jobs, job matches,
/// application records, and related events. It is typically configured and used with dependency injection in
/// application services. The model configuration defines relationships and constraints between entities to ensure data
/// integrity.</remarks>
public class JobTrackerDbContext : DbContext
{
    public DbSet<ScrapedJob> ScrapedJobs { get; set; }
    public DbSet<JobMatch> JobMatches { get; set; }
    public DbSet<ApplicationRecord> Applications { get; set; }
    public DbSet<ApplicationEvent> ApplicationEvents { get; set; }

    /// <summary>
    /// Initializes a new instance of the JobTrackerDbContext class using the specified options.
    /// </summary>
    /// <param name="options">The options to be used by the DbContext. Must not be null.</param>
    public JobTrackerDbContext(DbContextOptions<JobTrackerDbContext> options) : base(options) { }

    /// <summary>
    /// Configures the entity model for the context using the specified model builder.
    /// </summary>
    /// <remarks>This method is called by Entity Framework when the model for the context is being created.
    /// Override this method to configure entity relationships, indexes, and other model settings using the Fluent API.
    /// Call the base implementation if additional configuration is required.</remarks>
    /// <param name="b">The builder used to construct the model for the context. Provides configuration for entity types, relationships,
    /// and constraints.</param>
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ScrapedJob>()
         .HasIndex(j => j.JobId).IsUnique();

        b.Entity<ScrapedJob>()
         .HasOne(j => j.Match)
         .WithOne(m => m.ScrapedJob)
         .HasForeignKey<JobMatch>(m => m.ScrapedJobId);

        b.Entity<JobMatch>()
         .HasOne(m => m.Application)
         .WithOne(a => a.JobMatch)
         .HasForeignKey<ApplicationRecord>(a => a.JobMatchId);

        b.Entity<ApplicationRecord>()
         .HasMany(a => a.Events)
         .WithOne(e => e.Application)
         .HasForeignKey(e => e.ApplicationId);
    }
}
