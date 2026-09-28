namespace LoggingDemo.Functions.Models;

public sealed record WorkItemSubmission(
    string? Scenario,
    string? Payload,
    string? CorrelationId);
