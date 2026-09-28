using LoggingDemo.Functions.Models;

namespace LoggingDemo.Functions.Application;

public static class WorkItemSubmissionValidator
{
    public static SubmissionValidationResult Validate(WorkItemSubmission? submission)
    {
        if (submission is null)
        {
            return SubmissionValidationResult.Invalid("A JSON request body is required.");
        }

        if (!Enum.TryParse<ProcessingScenario>(submission.Scenario, ignoreCase: true, out var scenario) ||
            !Enum.IsDefined(scenario))
        {
            return SubmissionValidationResult.Invalid(
                "Scenario must be one of: success, validation, transient, poison, dependencyFailure.");
        }

        if (scenario == ProcessingScenario.Validation)
        {
            return SubmissionValidationResult.Invalid("The validation scenario is deterministically rejected.");
        }

        if (string.IsNullOrWhiteSpace(submission.Payload))
        {
            return SubmissionValidationResult.Invalid("Payload is required.");
        }

        return SubmissionValidationResult.Valid(scenario);
    }
}
