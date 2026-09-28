using Azure;
using LoggingDemo.Functions.Models;
using Microsoft.Extensions.Logging;

namespace LoggingDemo.Functions.Application;

public sealed class WorkItemService(
    IBlobRepository blobRepository,
    ILogger<WorkItemService> logger) : IWorkItemService
{
    public async Task ProcessAsync(
        WorkItemMessage workItem,
        long dequeueCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dequeueCount);

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["DemoLayer"] = "Application",
            ["CorrelationId"] = workItem.CorrelationId,
            ["MessageId"] = workItem.MessageId,
            ["Scenario"] = workItem.Scenario.ToString(),
            ["DequeueCount"] = dequeueCount
        });

        switch (workItem.Scenario)
        {
            case ProcessingScenario.Validation:
                throw new ValidationProcessingException(
                    "The queued work item selected the validation failure scenario.");

            case ProcessingScenario.Transient when dequeueCount <= 2:
                logger.LogWarning(
                    "Simulating transient failure on delivery {DequeueCount}.",
                    dequeueCount);
                throw new TransientProcessingException(
                    $"Simulated transient failure on delivery {dequeueCount}.");

            case ProcessingScenario.Poison:
                logger.LogError("Simulating a poison message failure.");
                throw new PoisonMessageException("Simulated poison message failure.");

            case ProcessingScenario.DependencyFailure:
                try
                {
                    await blobRepository.SaveAsync(workItem, cancellationToken);
                }
                catch (RequestFailedException exception)
                {
                    logger.LogError(
                        exception,
                        "The downstream blob dependency failed with status {DependencyStatus}.",
                        exception.Status);
                    throw new DependencyFailureException(
                        "Simulated blob dependency failure.",
                        exception);
                }

                throw new DependencyFailureException(
                    "The dependency failure scenario unexpectedly completed without an error.");

            case ProcessingScenario.Success:
            case ProcessingScenario.Transient:
                await blobRepository.SaveAsync(workItem, cancellationToken);
                logger.LogInformation("Work item processing completed.");
                return;

            default:
                throw new ValidationProcessingException(
                    $"Unsupported processing scenario '{workItem.Scenario}'.");
        }
    }
}
