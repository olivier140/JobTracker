// JobTracker.Core/ServiceRegistration.cs
using JobTracker.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for registering core Job Tracker services and ensuring database initialization within a
/// dependency injection container.
/// </summary>
/// <remarks>This class is intended to be used during application startup to configure required services for the
/// Job Tracker application. It includes methods for adding core services to an IServiceCollection and for ensuring that
/// the application's database is created and ready for use.</remarks>
public static class ServiceRegistration
{
    /// <summary>
    /// Adds the core job tracking services, including database context, job scrapers, and job matchers, to the
    /// specified service collection.
    /// </summary>
    /// <remarks>This method registers all required dependencies for the job tracking functionality, including
    /// the database context factory, job scrapers, and job matchers. It should be called during application startup to
    /// ensure all services are available for dependency injection.</remarks>
    /// <param name="services">The service collection to which the job tracking services will be added. Cannot be null.</param>
    /// <param name="settings">The application settings containing configuration values such as the database connection string and API keys.
    /// Cannot be null.</param>
    /// <returns>The same service collection instance with the job tracking services registered.</returns>
    public static IServiceCollection AddJobTrackerCore(this IServiceCollection services, AppSettings settings)
    {
        services.AddSingleton(settings);

        services.AddDbContextFactory<JobTrackerDbContext>(opts => opts.UseSqlServer(settings.ConnectionString));

        services.AddSingleton<MicrosoftJobsScraper>();
        services.AddSingleton<IJobScraper>(sp => sp.GetRequiredService<MicrosoftJobsScraper>());

        services.AddSingleton<ClaudeJobMatcher>(sp =>
            new ClaudeJobMatcher(
                settings.AnthropicApiKey,
                sp.GetRequiredService<IDbContextFactory<JobTrackerDbContext>>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ClaudeJobMatcher>>()
            ));
        services.AddSingleton<IJobMatcher>(sp => sp.GetRequiredService<ClaudeJobMatcher>());

        return services;
    }

    /// <summary>
    /// Ensures that the database for the application exists, creating it if it does not.
    /// </summary>
    /// <remarks>This method is typically called during application startup to guarantee that the database is
    /// created before use. If the database already exists, no action is taken.</remarks>
    /// <param name="sp">The service provider used to resolve the required database context factory. Cannot be null.</param>
    /// <returns>A task that represents the asynchronous operation. The task completes when the database has been ensured.</returns>
    public static async Task EnsureDatabaseAsync(IServiceProvider sp)
    {
        var factory = sp.GetRequiredService<IDbContextFactory<JobTrackerDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }

    /// <summary>
    /// Ensures the <c>nsb</c> schema exists in the database so that NServiceBus SQL Server
    /// transport tables can be created there.
    /// </summary>
    public static async Task EnsureNsbSchemaAsync(string connectionString)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'nsb')
                EXEC('CREATE SCHEMA nsb');
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}