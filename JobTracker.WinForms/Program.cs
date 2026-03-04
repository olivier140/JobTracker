// JobTracker.WinForms/Program.cs
using JobTracker.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Provides the entry point and application-wide service provider for the JobTracker application.
/// </summary>
/// <remarks>This class is responsible for initializing application configuration, setting up dependency
/// injection, and launching the main application form. It also exposes the application's service provider for use
/// throughout the application. This class is not intended to be instantiated.</remarks>
internal static class Program
{
    /// <summary>
    /// Gets the application's default service provider for resolving dependencies.
    /// </summary>
    /// <remarks>Use this property to access registered services throughout the application. The value is
    /// typically set during application startup and should not be modified at runtime.</remarks>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Serves as the main entry point for the application. Initializes configuration, validates required environment
    /// variables, sets up dependency injection, ensures the database is ready, and starts the application's main form.
    /// </summary>
    /// <remarks>This method requires the ANTHROPIC_API_KEY and JOBTRACKER_RESUME environment variables to be
    /// set before launching the application. If either variable is missing or empty, the application will not start.
    /// The method configures services and logging, binds application settings, and ensures the database is initialized
    /// before running the main form.</remarks>
    /// <returns>A task that represents the asynchronous operation of the application's startup and main event loop.</returns>
    [STAThread]
    static async Task Main()
    {
        ApplicationConfiguration.Initialize();

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        // Read API key environment variable
        string? apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.WriteLine("Environment variable ANTHROPIC_API_KEY not found.");
            return;
        }

        // Read resume location from environment
        string? resume = Environment.GetEnvironmentVariable("JOBTRACKER_RESUME");
        if (string.IsNullOrWhiteSpace(resume))
        {
            Console.Error.WriteLine("Error: Environment variable JOBTRACKER_RESUME is not set.");
            return;
        }

        var settings = new AppSettings();
        settings.AnthropicApiKey = apiKey;
        settings.Resume = resume;

        config.GetSection("AppSettings").Bind(settings);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJobTrackerCore(settings);
        services.AddTransient<MainForm>();
        Services = services.BuildServiceProvider();

        await ServiceRegistration.EnsureDatabaseAsync(Services);

        Application.Run(Services.GetRequiredService<MainForm>());
    }
}