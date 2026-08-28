using System.Text.Json;
using BudgetManager.Application.Classification;
using BudgetManager.Application.GitHub;
using BudgetManager.Application.Reports;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Infrastructure.Persistence;

public sealed class EfReportIngestionStore(BudgetManagerDbContext dbContext) : IReportIngestionStore
{
    private static readonly string[] AdditivePullRequestMetricKeys =
    [
        "pull_requests.total_applied_suggestions",
        "pull_requests.total_copilot_applied_suggestions",
        "pull_requests.total_copilot_suggestions",
        "pull_requests.total_created",
        "pull_requests.total_created_by_copilot",
        "pull_requests.total_merged",
        "pull_requests.total_merged_created_by_copilot",
        "pull_requests.total_merged_reviewed_by_copilot",
        "pull_requests.total_reviewed",
        "pull_requests.total_reviewed_by_copilot",
        "pull_requests.total_suggestions",
    ];

    private const string OrganizationRepositoryMetricSource = "copilot.RepositoriesDay.organization-aggregate";
    private const string TeamMetricSource = "copilot.UserTeamsDay.team-aggregate";

    public async Task SaveAsync(
        ReportIngestionBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var source = $"copilot.{batch.ReportKind}";
        if (batch.ReplaceExisting)
        {
            await dbContext.DailyMetrics
                .Where(item => item.EnterpriseId == batch.EnterpriseId
                    && item.Source == source
                    && item.MetricDay >= batch.ReportStartDay
                    && item.MetricDay <= batch.ReportEndDay)
                .ExecuteDeleteAsync(cancellationToken);
            if (batch.ReportKind == CopilotMetricReportKind.UserTeamsDay)
            {
                await dbContext.EntityMemberships
                    .Where(item => item.EnterpriseId == batch.EnterpriseId
                        && item.ParentScopeKind == ScopeKind.Team
                        && item.MemberScopeKind == ScopeKind.User
                        && item.ObservedOn >= batch.ReportStartDay
                        && item.ObservedOn <= batch.ReportEndDay)
                    .ExecuteDeleteAsync(cancellationToken);
            }
        }

        await UpsertEntitiesAsync(batch, cancellationToken);
        await AddMembershipsAsync(batch, cancellationToken);

        dbContext.IngestionManifests.Add(new IngestionManifestRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = batch.EnterpriseId,
            ReportType = batch.ReportKind.ToString(),
            ReportStartDay = batch.ReportStartDay,
            ReportEndDay = batch.ReportEndDay,
            BlobName = batch.BlobName,
            SourceUrlHash = batch.SourceUrlHash,
            ContentSha256 = batch.ContentSha256,
            Status = IngestionStatus.Succeeded,
            RecordCount = batch.Metrics.Count,
            StartedAt = batch.StartedAt,
            CompletedAt = batch.CompletedAt,
        });
        dbContext.DailyMetrics.AddRange(batch.Metrics.Select(metric => new DailyMetricRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = batch.EnterpriseId,
            ScopeKind = Enum.Parse<ScopeKind>(metric.ScopeKind, ignoreCase: true),
            ScopeExternalId = metric.ScopeExternalId,
            MetricKey = metric.MetricKey,
            MetricValue = metric.MetricValue,
            Availability = metric.Availability,
            AvailabilityDetail = metric.AvailabilityDetail,
            Source = metric.Source,
            MetricDay = metric.MetricDay,
            IngestedAt = batch.CompletedAt,
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
        if (batch.ReportKind == CopilotMetricReportKind.UserTeamsDay)
        {
            await RebuildTeamMetricsAsync(batch, cancellationToken);
        }
        else if (batch.ReportKind == CopilotMetricReportKind.RepositoriesDay)
        {
            await RebuildOrganizationRepositoryMetricsAsync(batch, cancellationToken);
        }

        dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = batch.EnterpriseId,
            EventType = "github.copilot.report.ingested",
            ActorType = "service",
            ActorId = "report-ingestion",
            TargetType = "report",
            TargetId = batch.ReportKind.ToString(),
            DataJson = JsonSerializer.Serialize(new
            {
                batch.ContentSha256,
                batch.BlobName,
                metricCount = batch.Metrics.Count,
            }),
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAt = batch.CompletedAt,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task UpsertEntitiesAsync(
        ReportIngestionBatch batch,
        CancellationToken cancellationToken)
    {
        var existingRecords = await dbContext.ManagedEntities
            .Where(item => item.EnterpriseId == batch.EnterpriseId)
            .ToListAsync(cancellationToken);
        var existing = existingRecords.ToDictionary(
            item => $"{item.ScopeKind}:{item.ExternalId}",
            StringComparer.OrdinalIgnoreCase);
        foreach (var entity in batch.Entities)
        {
            var key = $"{entity.ScopeKind}:{entity.ExternalId}";
            if (!existing.TryGetValue(key, out var record))
            {
                record = new ManagedEntityRecord
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = batch.EnterpriseId,
                    ScopeKind = Enum.Parse<ScopeKind>(entity.ScopeKind, true),
                    ExternalId = entity.ExternalId,
                    DisplayName = entity.DisplayName,
                };
                dbContext.ManagedEntities.Add(record);
                existing.Add(key, record);
            }

            record.DisplayName = entity.DisplayName;
            record.ParentScopeKind = entity.ParentScopeKind is null
                ? null
                : Enum.Parse<ScopeKind>(entity.ParentScopeKind, true);
            record.ParentExternalId = entity.ParentExternalId;
            record.MetadataJson = entity.MetadataJson;
            record.IsActive = true;
            record.ObservedAt = batch.CompletedAt;
        }
    }

    private async Task AddMembershipsAsync(
        ReportIngestionBatch batch,
        CancellationToken cancellationToken)
    {
        if (batch.Memberships.Count == 0)
        {
            return;
        }

        var existing = await dbContext.EntityMemberships
            .Where(item => item.EnterpriseId == batch.EnterpriseId
                && item.ObservedOn >= batch.ReportStartDay
                && item.ObservedOn <= batch.ReportEndDay)
            .ToListAsync(cancellationToken);
        var keys = existing.Select(CreateMembershipKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var membership in batch.Memberships)
        {
            var record = new EntityMembershipRecord
            {
                Id = Guid.NewGuid(),
                EnterpriseId = batch.EnterpriseId,
                ParentScopeKind = Enum.Parse<ScopeKind>(membership.ParentScopeKind, true),
                ParentExternalId = membership.ParentExternalId,
                MemberScopeKind = Enum.Parse<ScopeKind>(membership.MemberScopeKind, true),
                MemberExternalId = membership.MemberExternalId,
                ObservedOn = membership.ObservedOn,
            };
            if (keys.Add(CreateMembershipKey(record)))
            {
                dbContext.EntityMemberships.Add(record);
            }
        }
    }

    private async Task RebuildTeamMetricsAsync(
        ReportIngestionBatch batch,
        CancellationToken cancellationToken)
    {
        var teamMetricKeys = MetricCatalog.All
            .Where(item => item.SupportedScopes.Contains("Team", StringComparer.OrdinalIgnoreCase))
            .Select(item => item.Key)
            .ToArray();
        var contributions = await (
            from membership in dbContext.EntityMemberships.AsNoTracking()
            join metric in dbContext.DailyMetrics.AsNoTracking()
                on new
                {
                    membership.EnterpriseId,
                    ScopeExternalId = membership.MemberExternalId,
                    MetricDay = membership.ObservedOn,
                }
                equals new
                {
                    metric.EnterpriseId,
                    metric.ScopeExternalId,
                    metric.MetricDay,
                }
            where membership.EnterpriseId == batch.EnterpriseId
                && membership.ParentScopeKind == ScopeKind.Team
                && membership.MemberScopeKind == ScopeKind.User
                && membership.ObservedOn >= batch.ReportStartDay
                && membership.ObservedOn <= batch.ReportEndDay
                && metric.ScopeKind == ScopeKind.User
                && metric.Availability == "available"
                && teamMetricKeys.Contains(metric.MetricKey)
            select new
            {
                TeamId = membership.ParentExternalId,
                metric.MetricKey,
                metric.MetricValue,
                metric.MetricDay,
            }).ToListAsync(cancellationToken);
        await dbContext.DailyMetrics
            .Where(item => item.EnterpriseId == batch.EnterpriseId
                && item.Source == TeamMetricSource
                && item.MetricDay >= batch.ReportStartDay
                && item.MetricDay <= batch.ReportEndDay)
            .ExecuteDeleteAsync(cancellationToken);
        dbContext.DailyMetrics.AddRange(contributions
            .GroupBy(item => new { item.TeamId, item.MetricKey, item.MetricDay })
            .Select(group => new DailyMetricRecord
            {
                Id = Guid.NewGuid(),
                EnterpriseId = batch.EnterpriseId,
                ScopeKind = ScopeKind.Team,
                ScopeExternalId = group.Key.TeamId,
                MetricKey = group.Key.MetricKey,
                MetricValue = group.Sum(item => item.MetricValue),
                Availability = "available",
                Source = TeamMetricSource,
                MetricDay = group.Key.MetricDay,
                IngestedAt = batch.CompletedAt,
            }));
    }

    private async Task RebuildOrganizationRepositoryMetricsAsync(
        ReportIngestionBatch batch,
        CancellationToken cancellationToken)
    {
        var contributions = await (
            from repository in dbContext.ManagedEntities.AsNoTracking()
            join metric in dbContext.DailyMetrics.AsNoTracking()
                on new { repository.EnterpriseId, ScopeExternalId = repository.ExternalId }
                equals new { metric.EnterpriseId, metric.ScopeExternalId }
            where repository.EnterpriseId == batch.EnterpriseId
                && repository.ScopeKind == ScopeKind.Repository
                && repository.ParentScopeKind == ScopeKind.Organization
                && repository.ParentExternalId != null
                && metric.ScopeKind == ScopeKind.Repository
                && metric.MetricDay >= batch.ReportStartDay
                && metric.MetricDay <= batch.ReportEndDay
                && metric.Availability == "available"
                && AdditivePullRequestMetricKeys.Contains(metric.MetricKey)
            select new
            {
                OrganizationId = repository.ParentExternalId!,
                metric.MetricKey,
                metric.MetricValue,
                metric.MetricDay,
            }).ToListAsync(cancellationToken);
        await dbContext.DailyMetrics
            .Where(item => item.EnterpriseId == batch.EnterpriseId
                && item.Source == OrganizationRepositoryMetricSource
                && item.MetricDay >= batch.ReportStartDay
                && item.MetricDay <= batch.ReportEndDay)
            .ExecuteDeleteAsync(cancellationToken);
        dbContext.DailyMetrics.AddRange(contributions
            .GroupBy(item => new { item.OrganizationId, item.MetricKey, item.MetricDay })
            .Select(group => new DailyMetricRecord
            {
                Id = Guid.NewGuid(),
                EnterpriseId = batch.EnterpriseId,
                ScopeKind = ScopeKind.Organization,
                ScopeExternalId = group.Key.OrganizationId,
                MetricKey = group.Key.MetricKey,
                MetricValue = group.Sum(item => item.MetricValue),
                Availability = "available",
                Source = OrganizationRepositoryMetricSource,
                MetricDay = group.Key.MetricDay,
                IngestedAt = batch.CompletedAt,
            }));
    }

    private static string CreateMembershipKey(EntityMembershipRecord item) =>
        $"{item.ParentScopeKind}:{item.ParentExternalId}:{item.MemberScopeKind}:{item.MemberExternalId}:{item.ObservedOn:yyyy-MM-dd}";
}
