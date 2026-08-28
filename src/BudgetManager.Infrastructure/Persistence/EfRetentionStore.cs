using System.Text.Json;
using BudgetManager.Application.Operations;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Infrastructure.Persistence;

public sealed class EfRetentionStore(BudgetManagerDbContext dbContext) : IRetentionStore
{
    private bool UsesSqlite => string.Equals(
        dbContext.Database.ProviderName,
        "Microsoft.EntityFrameworkCore.Sqlite",
        StringComparison.Ordinal);

    public async Task<RetentionConfiguration?> GetConfigurationAsync(
        Guid enterpriseId,
        CancellationToken cancellationToken = default) => await dbContext.RetentionPolicies
        .AsNoTracking()
        .Where(item => item.EnterpriseId == enterpriseId)
        .Select(item => new RetentionConfiguration(
            item.RawReportDays,
            item.UserMetricDays,
            item.AggregateMetricDays,
            item.NotificationDays,
            item.AuditDays,
            item.LegalHold))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task<RetentionPreview> PreviewAsync(
        Guid enterpriseId,
        RetentionConfiguration configuration,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken = default)
    {
        var rawCutoff = DateOnly.FromDateTime(evaluatedAt.UtcDateTime.AddDays(-configuration.RawReportDays));
        var userCutoff = DateOnly.FromDateTime(evaluatedAt.UtcDateTime.AddDays(-configuration.UserMetricDays));
        var aggregateCutoff = DateOnly.FromDateTime(evaluatedAt.UtcDateTime.AddDays(-configuration.AggregateMetricDays));
        var classificationCutoff = evaluatedAt.AddDays(-configuration.AggregateMetricDays);
        var notificationCutoff = evaluatedAt.AddDays(-configuration.NotificationDays);
        var auditCutoff = evaluatedAt.AddDays(-configuration.AuditDays);
        int classifications;
        int notifications;
        int auditEvents;
        if (UsesSqlite)
        {
            classifications = (await dbContext.ClassificationSnapshots
                .Where(item => item.EnterpriseId == enterpriseId)
                .Select(item => item.EvaluatedAt)
                .ToListAsync(cancellationToken))
                .Count(item => item < classificationCutoff);
            notifications = (await dbContext.NotificationDeliveries
                .Where(item => item.EnterpriseId == enterpriseId)
                .Select(item => item.CreatedAt)
                .ToListAsync(cancellationToken))
                .Count(item => item < notificationCutoff);
            auditEvents = (await dbContext.AuditEvents
                .Where(item => item.EnterpriseId == enterpriseId)
                .Select(item => item.OccurredAt)
                .ToListAsync(cancellationToken))
                .Count(item => item < auditCutoff);
        }
        else
        {
            classifications = await dbContext.ClassificationSnapshots.CountAsync(
                item => item.EnterpriseId == enterpriseId && item.EvaluatedAt < classificationCutoff,
                cancellationToken);
            notifications = await dbContext.NotificationDeliveries.CountAsync(
                item => item.EnterpriseId == enterpriseId && item.CreatedAt < notificationCutoff,
                cancellationToken);
            auditEvents = await dbContext.AuditEvents.CountAsync(
                item => item.EnterpriseId == enterpriseId && item.OccurredAt < auditCutoff,
                cancellationToken);
        }

        return new RetentionPreview(
            await dbContext.IngestionManifests.CountAsync(item => item.EnterpriseId == enterpriseId && item.ReportEndDay < rawCutoff, cancellationToken),
            await dbContext.DailyMetrics.CountAsync(item => item.EnterpriseId == enterpriseId && item.ScopeKind == ScopeKind.User && item.MetricDay < userCutoff, cancellationToken),
            await dbContext.DailyMetrics.CountAsync(item => item.EnterpriseId == enterpriseId && item.ScopeKind != ScopeKind.User && item.MetricDay < aggregateCutoff, cancellationToken),
            classifications,
            notifications,
            auditEvents);
    }

    public async Task ApplyAsync(
        Guid enterpriseId,
        RetentionConfiguration configuration,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var rawCutoff = DateOnly.FromDateTime(evaluatedAt.UtcDateTime.AddDays(-configuration.RawReportDays));
        var userCutoff = DateOnly.FromDateTime(evaluatedAt.UtcDateTime.AddDays(-configuration.UserMetricDays));
        var aggregateCutoff = DateOnly.FromDateTime(evaluatedAt.UtcDateTime.AddDays(-configuration.AggregateMetricDays));
        var classificationCutoff = evaluatedAt.AddDays(-configuration.AggregateMetricDays);
        var notificationCutoff = evaluatedAt.AddDays(-configuration.NotificationDays);
        var auditCutoff = evaluatedAt.AddDays(-configuration.AuditDays);
        await dbContext.IngestionManifests.Where(item => item.EnterpriseId == enterpriseId && item.ReportEndDay < rawCutoff).ExecuteDeleteAsync(cancellationToken);
        await dbContext.DailyMetrics.Where(item => item.EnterpriseId == enterpriseId && item.ScopeKind == ScopeKind.User && item.MetricDay < userCutoff).ExecuteDeleteAsync(cancellationToken);
        await dbContext.DailyMetrics.Where(item => item.EnterpriseId == enterpriseId && item.ScopeKind != ScopeKind.User && item.MetricDay < aggregateCutoff).ExecuteDeleteAsync(cancellationToken);
        if (UsesSqlite)
        {
            dbContext.ClassificationSnapshots.RemoveRange((await dbContext.ClassificationSnapshots
                .Where(item => item.EnterpriseId == enterpriseId)
                .ToListAsync(cancellationToken))
                .Where(item => item.EvaluatedAt < classificationCutoff));
            dbContext.NotificationDeliveries.RemoveRange((await dbContext.NotificationDeliveries
                .Where(item => item.EnterpriseId == enterpriseId)
                .ToListAsync(cancellationToken))
                .Where(item => item.CreatedAt < notificationCutoff));
            dbContext.AuditEvents.RemoveRange((await dbContext.AuditEvents
                .Where(item => item.EnterpriseId == enterpriseId)
                .ToListAsync(cancellationToken))
                .Where(item => item.OccurredAt < auditCutoff));
        }
        else
        {
            await dbContext.ClassificationSnapshots.Where(item => item.EnterpriseId == enterpriseId && item.EvaluatedAt < classificationCutoff).ExecuteDeleteAsync(cancellationToken);
            await dbContext.NotificationDeliveries.Where(item => item.EnterpriseId == enterpriseId && item.CreatedAt < notificationCutoff).ExecuteDeleteAsync(cancellationToken);
            await dbContext.AuditEvents.Where(item => item.EnterpriseId == enterpriseId && item.OccurredAt < auditCutoff).ExecuteDeleteAsync(cancellationToken);
        }

        dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = enterpriseId,
            EventType = "retention.applied",
            ActorType = "service",
            ActorId = "retention-worker",
            TargetType = "enterprise",
            TargetId = enterpriseId.ToString("N"),
            DataJson = "{}",
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAt = evaluatedAt,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordPreviewAsync(
        Guid enterpriseId,
        RetentionConfiguration configuration,
        RetentionPreview preview,
        DateTimeOffset evaluatedAt,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = enterpriseId,
            EventType = "retention.previewed",
            ActorType = "service",
            ActorId = "retention-worker",
            TargetType = "enterprise",
            TargetId = enterpriseId.ToString("N"),
            DataJson = JsonSerializer.Serialize(new
            {
                configuration,
                preview,
                dryRun,
                blockedByLegalHold = configuration.LegalHold,
            }),
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAt = evaluatedAt,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
