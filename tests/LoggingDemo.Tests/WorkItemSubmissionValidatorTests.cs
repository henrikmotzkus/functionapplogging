using LoggingDemo.Functions.Application;
using LoggingDemo.Functions.Models;
using Xunit;

namespace LoggingDemo.Tests;

public sealed class WorkItemSubmissionValidatorTests
{
    [Fact]
    public void Validate_AcceptsSupportedScenarioAndPayload()
    {
        var result = WorkItemSubmissionValidator.Validate(
            new WorkItemSubmission("success", "payload", "correlation-id"));

        Assert.True(result.IsValid);
        Assert.Equal(ProcessingScenario.Success, result.Scenario);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Validate_RejectsValidationScenarioDeterministically()
    {
        var result = WorkItemSubmissionValidator.Validate(
            new WorkItemSubmission("validation", "payload", "correlation-id"));

        Assert.False(result.IsValid);
        Assert.Equal("The validation scenario is deterministically rejected.", result.Error);
    }

    [Theory]
    [InlineData(null, "payload", "Scenario must be one of: success, validation, transient, poison, dependencyFailure.")]
    [InlineData("unknown", "payload", "Scenario must be one of: success, validation, transient, poison, dependencyFailure.")]
    [InlineData("success", null, "Payload is required.")]
    [InlineData("success", "  ", "Payload is required.")]
    public void Validate_RejectsInvalidSubmissions(
        string? scenario,
        string? payload,
        string expectedError)
    {
        var result = WorkItemSubmissionValidator.Validate(
            new WorkItemSubmission(scenario, payload, null));

        Assert.False(result.IsValid);
        Assert.Equal(expectedError, result.Error);
    }
}
