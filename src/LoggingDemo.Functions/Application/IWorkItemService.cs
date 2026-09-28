using LoggingDemo.Functions.Models;

namespace LoggingDemo.Functions.Application;

public interface IWorkItemService
{
    Task ProcessAsync(
        WorkItemMessage workItem,
        long dequeueCount,
        CancellationToken cancellationToken);
}
