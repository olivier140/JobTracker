namespace JobTracker.WordExport;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering Word-export services into the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IResumeExporter"/> as a singleton backed by <see cref="ResumeExporter"/>.
    /// Requires <c>AddJobTrackerCore</c> to have been called first so that
    /// <c>IDbContextFactory&lt;JobTrackerDbContext&gt;</c> and <c>AppSettings</c> are available.
    /// </summary>
    public static IServiceCollection AddWordExport(this IServiceCollection services)
    {
        services.AddSingleton<IResumeExporter, ResumeExporter>();
        return services;
    }
}
