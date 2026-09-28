using LoggingDemo.Functions.Models;

namespace LoggingDemo.Functions.Application;

public interface IBlobRepository
{
    Task SaveAsync(WorkItemMessage workItem, CancellationToken cancellationToken);
}
