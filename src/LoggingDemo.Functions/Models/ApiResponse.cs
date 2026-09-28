namespace LoggingDemo.Functions.Models;

public sealed record ApiResponse(
    string Status,
    string CorrelationId,
    Guid? MessageId = null,
    string? Error = null);
