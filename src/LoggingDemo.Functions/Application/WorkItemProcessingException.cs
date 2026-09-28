namespace LoggingDemo.Functions.Application;

public abstract class WorkItemProcessingException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class ValidationProcessingException(string message) : WorkItemProcessingException(message);

public sealed class TransientProcessingException(string message) : WorkItemProcessingException(message);

public sealed class PoisonMessageException(string message) : WorkItemProcessingException(message);

public sealed class DependencyFailureException(string message, Exception? innerException = null)
    : WorkItemProcessingException(message, innerException);
