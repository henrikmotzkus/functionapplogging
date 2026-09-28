namespace LoggingDemo.Functions.Models;

public enum ProcessingScenario
{
    Success,
    Validation,
    Transient,
    Poison,
    DependencyFailure
}
