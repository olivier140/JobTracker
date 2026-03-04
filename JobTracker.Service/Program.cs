// JobTracker.Service/Program.cs
using JobTracker.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

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

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(opts => opts.ServiceName = "JobTracker")
    .ConfigureAppConfiguration(cfg => cfg
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile("appsettings.Service.json", optional: true)
        .AddEnvironmentVariables())
    .ConfigureServices((ctx, services) =>
    {
        var settings = new AppSettings();
        settings.AnthropicApiKey = apiKey;
        settings.Resume = resume;
        ctx.Configuration.GetSection("AppSettings").Bind(settings);

        services.AddJobTrackerCore(settings);
        services.AddHostedService<ScraperWorker>();
    })
    .ConfigureLogging(log =>
    {
        log.AddConsole();
        log.AddEventLog(cfg => cfg.SourceName = "JobTracker");
    })
    .Build();

await ServiceRegistration.EnsureDatabaseAsync(host.Services);
await host.RunAsync();
