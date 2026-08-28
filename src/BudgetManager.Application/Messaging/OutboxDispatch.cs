using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BudgetManager.Application.Messaging;

public sealed record OutboxEnvelope(
    Guid Id,
    Guid EnterpriseId,
    string EventType,
    string EventFingerprint,
    string PayloadJson,
    int AttemptCount,
    DateTimeOffset OccurredAt);

public sealed record OutboxDispatchResult(int Published, int Failed);

public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxEnvelope>> GetPendingAsync(
        int maximumCount,
        CancellationToken cancellationToken = default);

    Task MarkProcessedAsync(
        Guid messageId,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        Guid messageId,
        string errorCode,
        CancellationToken cancellationToken = default);
}

public interface IIntegrationEventPublisher
{
    Task PublishAsync(OutboxEnvelope message, CancellationToken cancellationToken = default);
}

public sealed partial class OutboxDispatcher
{
    private readonly ILogger<OutboxDispatcher> logger;
    private readonly IOutboxStore store;
    private readonly IIntegrationEventPublisher publisher;
    private readonly TimeProvider timeProvider;

    public OutboxDispatcher(
        IOutboxStore store,
        IIntegrationEventPublisher publisher,
        TimeProvider? timeProvider = null,
        ILogger<OutboxDispatcher>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(publisher);
        this.logger = logger ?? NullLogger<OutboxDispatcher>.Instance;
        this.store = store;
        this.publisher = publisher;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<OutboxDispatchResult> DispatchAsync(
        int maximumCount = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);
        var messages = await store.GetPendingAsync(maximumCount, cancellationToken);
        var published = 0;
        var failed = 0;
        foreach (var message in messages)
        {
            try
            {
                await publisher.PublishAsync(message, cancellationToken);
                await store.MarkProcessedAsync(message.Id, timeProvider.GetUtcNow(), cancellationToken);
                published++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogPublishFailure(
                    logger,
                    exception,
                    message.Id,
                    message.EventType,
                    message.AttemptCount + 1);
                await store.MarkFailedAsync(message.Id, exception.GetType().Name, cancellationToken);
                failed++;
            }
        }

        return new OutboxDispatchResult(published, failed);
    }

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Error,
        Message = "Outbox message {MessageId} for event {EventType} failed on attempt {AttemptCount}")]
    private static partial void LogPublishFailure(
        ILogger logger,
        Exception exception,
        Guid messageId,
        string eventType,
        int attemptCount);
}
