using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace LoggingDemo.Functions.Functions;

public sealed class SubmitWorkItemOutput
{
    [QueueOutput(StorageNames.WorkItemsQueue, Connection = StorageNames.Connection)]
    public string? QueueMessage { get; init; }

    public required HttpResponseData HttpResponse { get; init; }
}
