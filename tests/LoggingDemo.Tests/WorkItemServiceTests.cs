using Azure;
using LoggingDemo.Functions.Application;
using LoggingDemo.Functions.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoggingDemo.Tests;

public sealed class WorkItemServiceTests
{
    [Fact]
    public async Task ProcessAsync_Success_PersistsWorkItem()
    {
        var repository = new RecordingBlobRepository();
        var service = CreateService(repository);
        var workItem = CreateWorkItem(ProcessingScenario.Success);

        await service.ProcessAsync(workItem, 1, CancellationToken.None);

        Assert.Same(workItem, Assert.Single(repository.SavedItems));
    }

    [Fact]
    public async Task ProcessAsync_Validation_ThrowsWithoutPersisting()
    {
        var repository = new RecordingBlobRepository();
        var service = CreateService(repository);

        await Assert.ThrowsAsync<ValidationProcessingException>(
            () => service.ProcessAsync(
                CreateWorkItem(ProcessingScenario.Validation),
                1,
                CancellationToken.None));

        Assert.Empty(repository.SavedItems);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ProcessAsync_TransientFirstTwoDeliveries_Throws(int dequeueCount)
    {
        var repository = new RecordingBlobRepository();
        var service = CreateService(repository);

        await Assert.ThrowsAsync<TransientProcessingException>(
            () => service.ProcessAsync(
                CreateWorkItem(ProcessingScenario.Transient),
                dequeueCount,
                CancellationToken.None));

        Assert.Empty(repository.SavedItems);
    }

    [Fact]
    public async Task ProcessAsync_TransientThirdDelivery_PersistsWorkItem()
    {
        var repository = new RecordingBlobRepository();
        var service = CreateService(repository);
        var workItem = CreateWorkItem(ProcessingScenario.Transient);

        await service.ProcessAsync(workItem, 3, CancellationToken.None);

        Assert.Same(workItem, Assert.Single(repository.SavedItems));
    }

    [Fact]
    public async Task ProcessAsync_Poison_AlwaysThrows()
    {
        var repository = new RecordingBlobRepository();
        var service = CreateService(repository);

        await Assert.ThrowsAsync<PoisonMessageException>(
            () => service.ProcessAsync(
                CreateWorkItem(ProcessingScenario.Poison),
                5,
                CancellationToken.None));

        Assert.Empty(repository.SavedItems);
    }

    [Fact]
    public async Task ProcessAsync_DependencyFailure_AlwaysThrows()
    {
        var repository = new RecordingBlobRepository(throwDependencyFailure: true);
        var service = CreateService(repository);
        var workItem = CreateWorkItem(ProcessingScenario.DependencyFailure);

        var exception = await Assert.ThrowsAsync<DependencyFailureException>(
            () => service.ProcessAsync(
                workItem,
                1,
                CancellationToken.None));

        Assert.IsType<RequestFailedException>(exception.InnerException);
        Assert.Same(workItem, Assert.Single(repository.SavedItems));
    }

    private static WorkItemService CreateService(IBlobRepository repository) =>
        new(repository, NullLogger<WorkItemService>.Instance);

    private static WorkItemMessage CreateWorkItem(ProcessingScenario scenario) =>
        new(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            "correlation-id",
            scenario,
            "payload",
            new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.Zero));

    private sealed class RecordingBlobRepository(bool throwDependencyFailure = false) : IBlobRepository
    {
        public List<WorkItemMessage> SavedItems { get; } = [];

        public Task SaveAsync(WorkItemMessage workItem, CancellationToken cancellationToken)
        {
            SavedItems.Add(workItem);

            if (throwDependencyFailure)
            {
                throw new RequestFailedException(404, "Simulated missing blob.");
            }

            return Task.CompletedTask;
        }
    }
}
