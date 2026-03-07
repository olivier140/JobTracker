// JobTracker.Messaging/Program.cs
// NServiceBus receiver endpoint that processes commands sent after resume exports.
using JobTracker.Core;
using Microsoft.Extensions.Configuration;
using NServiceBus;

// Pre-load settings to create the nsb schema before NServiceBus starts
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var appSettings = new AppSettings();
config.GetSection("AppSettings").Bind(appSettings);

await ServiceRegistration.EnsureNsbSchemaAsync(appSettings.ConnectionString);

// Configure NServiceBus endpoint
var endpointConfig = new EndpointConfiguration("JobTracker.Messaging");

var transport = new SqlServerTransport(appSettings.ConnectionString);
transport.DefaultSchema = "nsb";
endpointConfig.UseTransport(transport);

endpointConfig.Conventions()
    .DefiningCommandsAs(type => type.Namespace == "JobTracker.Core.Commands");

endpointConfig.EnableInstallers();

// Build and run host
var hostBuilder = Host.CreateApplicationBuilder(args);
hostBuilder.UseNServiceBus(endpointConfig);

var host = hostBuilder.Build();
await host.RunAsync();
