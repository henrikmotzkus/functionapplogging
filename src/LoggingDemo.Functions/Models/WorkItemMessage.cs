namespace LoggingDemo.Functions.Models;

public sealed record WorkItemMessage(
    Guid MessageId,
    string CorrelationId,
    ProcessingScenario Scenario,
    string Payload,
    DateTimeOffset EnqueuedAtUtc);
