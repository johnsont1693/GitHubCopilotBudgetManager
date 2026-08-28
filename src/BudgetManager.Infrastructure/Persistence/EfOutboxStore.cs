using BudgetManager.Application.Messaging;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Infrastructure.Persistence;

public sealed class EfOutboxStore(BudgetManagerDbContext dbContext) : IOutboxStore
{
    public async Task<IReadOnlyList<OutboxEnvelope>> GetPendingAsync(
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);
        return await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(item => item.Status == OutboxStatus.Pending || item.Status == OutboxStatus.Failed)
            .OrderBy(item => item.OccurredAt)
            .Take(maximumCount)
            .Select(item => new OutboxEnvelope(
                item.Id,
                item.EnterpriseId,
                item.EventType,
                item.EventFingerprint,
                item.PayloadJson,
                item.AttemptCount,
                item.OccurredAt))
            .ToListAsync(cancellationToken);
    }

    public Task MarkProcessedAsync(
        Guid messageId,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken = default) => dbContext.OutboxMessages
        .Where(item => item.Id == messageId)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.Status, OutboxStatus.Processed)
            .SetProperty(item => item.ProcessedAt, processedAt)
            .SetProperty(item => item.LastErrorCode, (string?)null), cancellationToken);

    public Task MarkFailedAsync(
        Guid messageId,
        string errorCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        return dbContext.OutboxMessages
            .Where(item => item.Id == messageId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, OutboxStatus.Failed)
                .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1)
                .SetProperty(item => item.LastErrorCode, errorCode), cancellationToken);
    }
}
