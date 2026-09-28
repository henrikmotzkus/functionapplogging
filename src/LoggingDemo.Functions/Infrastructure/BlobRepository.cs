using System.Text.Json;
using Azure.Storage.Blobs;
using LoggingDemo.Functions.Application;
using LoggingDemo.Functions.Models;
using LoggingDemo.Functions.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LoggingDemo.Functions.Infrastructure;

public sealed class BlobRepository(
    BlobServiceClient blobServiceClient,
    IConfiguration configuration,
    ILogger<BlobRepository> logger) : IBlobRepository
{
    public async Task SaveAsync(WorkItemMessage workItem, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        var containerName = configuration["DemoResultsContainerName"];
        if (string.IsNullOrWhiteSpace(containerName))
        {
            containerName = StorageNames.DefaultProcessedItemsContainer;
        }

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["DemoLayer"] = "Infrastructure",
            ["CorrelationId"] = workItem.CorrelationId,
            ["MessageId"] = workItem.MessageId,
            ["Scenario"] = workItem.Scenario.ToString()
        });

        var container = blobServiceClient.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        if (workItem.Scenario == ProcessingScenario.DependencyFailure)
        {
            logger.LogWarning("Calling a deliberately missing blob to demonstrate a failed dependency.");
            await container
                .GetBlobClient($"missing-{workItem.MessageId:D}.json")
                .DownloadContentAsync(cancellationToken);
            return;
        }

        var blobName = $"{workItem.MessageId:D}.json";
        var blob = container.GetBlobClient(blobName);
        var content = BinaryData.FromString(JsonSerializer.Serialize(workItem, JsonDefaults.Options));

        await blob.UploadAsync(content, overwrite: true, cancellationToken);

        logger.LogInformation(
            "Persisted processed work item to container {ContainerName} as blob {BlobName}.",
            containerName,
            blobName);
    }
}
