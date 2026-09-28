using LoggingDemo.Functions.Models;

namespace LoggingDemo.Functions.Application;

public sealed record SubmissionValidationResult(
    bool IsValid,
    ProcessingScenario Scenario,
    string? Error)
{
    public static SubmissionValidationResult Valid(ProcessingScenario scenario) =>
        new(true, scenario, null);

    public static SubmissionValidationResult Invalid(string error) =>
        new(false, default, error);
}
