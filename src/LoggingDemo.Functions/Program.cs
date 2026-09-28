using Azure.Identity;
using Azure.Storage.Blobs;
using LoggingDemo.Functions;
using LoggingDemo.Functions.Application;
using LoggingDemo.Functions.Infrastructure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        services.Configure<LoggerFilterOptions>(options =>
        {
            var applicationInsightsRule = options.Rules.FirstOrDefault(
                static rule => rule.ProviderName ==
                    "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");

            if (applicationInsightsRule is not null)
            {
                options.Rules.Remove(applicationInsightsRule);
            }
        });

        services.AddSingleton(static serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var serviceUri = configuration[$"{StorageNames.Connection}:blobServiceUri"];

            if (Uri.TryCreate(serviceUri, UriKind.Absolute, out var blobServiceUri))
            {
                var managedIdentityClientId =
                    configuration[$"{StorageNames.Connection}:clientId"];
                var credentialOptions = new DefaultAzureCredentialOptions();
                if (!string.IsNullOrWhiteSpace(managedIdentityClientId))
                {
                    credentialOptions.ManagedIdentityClientId = managedIdentityClientId;
                }

                return new BlobServiceClient(
                    blobServiceUri,
                    new DefaultAzureCredential(credentialOptions));
            }

            var connectionString = configuration[StorageNames.Connection];
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                return new BlobServiceClient(connectionString);
            }

            throw new InvalidOperationException(
                $"Configure '{StorageNames.Connection}' as a connection string or " +
                $"'{StorageNames.Connection}__blobServiceUri' for managed identity.");
        });

        services.AddSingleton<IBlobRepository, BlobRepository>();
        services.AddSingleton<IWorkItemService, WorkItemService>();
    })
    .Build();

host.Run();
