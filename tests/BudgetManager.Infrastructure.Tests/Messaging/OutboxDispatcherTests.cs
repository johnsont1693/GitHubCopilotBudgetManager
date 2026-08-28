using BudgetManager.Application.Messaging;

namespace BudgetManager.Infrastructure.Tests.Messaging;

public sealed class OutboxDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_marks_each_message_according_to_publish_result()
    {
        var first = CreateEnvelope("first");
        var second = CreateEnvelope("second");
        var store = new RecordingStore([first, second]);
        var publisher = new SelectivePublisher(second.Id);
        var dispatcher = new OutboxDispatcher(store, publisher);

        var result = await dispatcher.DispatchAsync();

        Assert.Equal(1, result.Published);
        Assert.Equal(1, result.Failed);
        Assert.Contains(first.Id, store.Processed);
        Assert.Contains(second.Id, store.Failed);
    }

    private static OutboxEnvelope CreateEnvelope(string fingerprint) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "budget.change.requested.v1",
        fingerprint,
        "{}",
        0,
        DateTimeOffset.UtcNow);

    private sealed class RecordingStore(IReadOnlyList<OutboxEnvelope> messages) : IOutboxStore
    {
        public List<Guid> Processed { get; } = [];

        public List<Guid> Failed { get; } = [];

        public Task<IReadOnlyList<OutboxEnvelope>> GetPendingAsync(int maximumCount, CancellationToken cancellationToken = default) => Task.FromResult(messages);

        public Task MarkProcessedAsync(Guid messageId, DateTimeOffset processedAt, CancellationToken cancellationToken = default)
        {
            Processed.Add(messageId);
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(Guid messageId, string errorCode, CancellationToken cancellationToken = default)
        {
            Failed.Add(messageId);
            return Task.CompletedTask;
        }
    }

    private sealed class SelectivePublisher(Guid failingId) : IIntegrationEventPublisher
    {
        public Task PublishAsync(OutboxEnvelope message, CancellationToken cancellationToken = default) =>
            message.Id == failingId
                ? throw new InvalidOperationException("simulated")
                : Task.CompletedTask;
    }
}
