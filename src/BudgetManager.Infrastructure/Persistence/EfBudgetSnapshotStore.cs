using System.Text.Json;
using BudgetManager.Application.Budgets;
using BudgetManager.Application.GitHub;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Infrastructure.Persistence;

public sealed class EfBudgetSnapshotStore(BudgetManagerDbContext dbContext) : IBudgetSnapshotStore
{
    private const string CostCenterMetricSource = "github.cost-centers";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SaveAsync(
        BudgetSynchronizationBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var budget in batch.Budgets)
        {
            dbContext.BudgetSnapshots.Add(new BudgetSnapshotRecord
            {
                Id = Guid.NewGuid(),
                EnterpriseId = batch.EnterpriseId,
                BudgetId = budget.Id,
                BudgetType = budget.BudgetType,
                BudgetScope = budget.BudgetScope,
                BudgetEntityName = budget.BudgetEntityName,
                UserLogin = budget.User,
                BudgetAmount = budget.BudgetAmount,
                ConsumedAmount = budget.ConsumedAmount,
                ProductSku = budget.BudgetProductSku,
                PreventFurtherUsage = budget.PreventFurtherUsage,
                AlertingJson = JsonSerializer.Serialize(budget.BudgetAlerting, JsonOptions),
                IsEffective = batch.EffectiveBudget?.Id == budget.Id,
                ObservedAt = batch.ObservedAt,
            });
        }

        await SaveCostCentersAsync(batch, cancellationToken);
        foreach (var observation in batch.UserStates)
        {
            if (string.IsNullOrWhiteSpace(observation.State.User))
            {
                throw new InvalidOperationException(
                    $"GitHub returned a user state without a login for budget '{observation.BudgetId}'.");
            }

            dbContext.BudgetUserStateSnapshots.Add(new BudgetUserStateSnapshotRecord
            {
                Id = Guid.NewGuid(),
                EnterpriseId = batch.EnterpriseId,
                BudgetId = observation.BudgetId,
                UserLogin = observation.State.User,
                ConsumedAmount = observation.State.ConsumedAmount,
                TargetAmount = observation.State.TargetAmount,
                OverrideBudgetId = observation.State.OverrideBudgetId,
                ObservedAt = batch.ObservedAt,
            });
        }

        await SaveBillingExportsAsync(batch, cancellationToken);

        dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = batch.EnterpriseId,
            EventType = "github.budgets.synchronized",
            ActorType = "service",
            ActorId = "financial-sync",
            TargetType = "enterprise",
            TargetId = batch.EnterpriseSlug,
            DataJson = JsonSerializer.Serialize(new
            {
                budgetCount = batch.Budgets.Count,
                costCenterCount = batch.CostCenters.Count,
                userStateCount = batch.UserStates.Count,
                usageReportExportCount = batch.UsageReportExports.Count,
                effectiveBudgetId = batch.EffectiveBudget?.Id,
                observedAt = batch.ObservedAt,
            }, JsonOptions),
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAt = batch.ObservedAt,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task SaveCostCentersAsync(
        BudgetSynchronizationBatch batch,
        CancellationToken cancellationToken)
    {
        var existingCostCenters = await dbContext.ManagedEntities
            .Where(item => item.EnterpriseId == batch.EnterpriseId
                && item.ScopeKind == ScopeKind.CostCenter)
            .ToListAsync(cancellationToken);
        var byId = existingCostCenters.ToDictionary(item => item.ExternalId, StringComparer.OrdinalIgnoreCase);
        var observedIds = batch.CostCenters.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var missing in existingCostCenters.Where(item => !observedIds.Contains(item.ExternalId)))
        {
            missing.IsActive = false;
            missing.ObservedAt = batch.ObservedAt;
        }

        var observedDay = DateOnly.FromDateTime(batch.ObservedAt.UtcDateTime);
        await dbContext.EntityMemberships
            .Where(item => item.EnterpriseId == batch.EnterpriseId
                && item.ParentScopeKind == ScopeKind.CostCenter
                && item.ObservedOn == observedDay)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.DailyMetrics
            .Where(item => item.EnterpriseId == batch.EnterpriseId
                && item.ScopeKind == ScopeKind.CostCenter
                && item.Source == CostCenterMetricSource
                && item.MetricDay == observedDay)
            .ExecuteDeleteAsync(cancellationToken);
        var knownEntities = (await dbContext.ManagedEntities
                .Where(item => item.EnterpriseId == batch.EnterpriseId)
                .ToListAsync(cancellationToken))
            .ToDictionary(item => $"{item.ScopeKind}:{item.ExternalId}", StringComparer.OrdinalIgnoreCase);
        foreach (var costCenter in batch.CostCenters)
        {
            if (!byId.TryGetValue(costCenter.Id, out var record))
            {
                record = new ManagedEntityRecord
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = batch.EnterpriseId,
                    ScopeKind = ScopeKind.CostCenter,
                    ExternalId = costCenter.Id,
                    DisplayName = costCenter.Name,
                    ParentScopeKind = ScopeKind.Enterprise,
                    ParentExternalId = batch.EnterpriseSlug,
                };
                dbContext.ManagedEntities.Add(record);
                byId.Add(costCenter.Id, record);
                knownEntities[$"{ScopeKind.CostCenter}:{costCenter.Id}"] = record;
            }

            record.DisplayName = costCenter.Name;
            record.ParentScopeKind = ScopeKind.Enterprise;
            record.ParentExternalId = batch.EnterpriseSlug;
            record.MetadataJson = CreateCostCenterMetadata(record.MetadataJson, costCenter);
            record.IsActive = costCenter.State.Equals("active", StringComparison.OrdinalIgnoreCase);
            record.ObservedAt = batch.ObservedAt;
            foreach (var resource in costCenter.Resources)
            {
                if (!TryMapResourceScope(resource.Type, out var resourceScope))
                {
                    continue;
                }

                var entityKey = $"{resourceScope}:{resource.Name}";
                if (!knownEntities.TryGetValue(entityKey, out var resourceEntity))
                {
                    resourceEntity = new ManagedEntityRecord
                    {
                        Id = Guid.NewGuid(),
                        EnterpriseId = batch.EnterpriseId,
                        ScopeKind = resourceScope,
                        ExternalId = resource.Name,
                        DisplayName = resource.Name,
                        IsActive = true,
                        ObservedAt = batch.ObservedAt,
                    };
                    dbContext.ManagedEntities.Add(resourceEntity);
                    knownEntities.Add(entityKey, resourceEntity);
                }

                dbContext.EntityMemberships.Add(new EntityMembershipRecord
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = batch.EnterpriseId,
                    ParentScopeKind = ScopeKind.CostCenter,
                    ParentExternalId = costCenter.Id,
                    MemberScopeKind = resourceScope,
                    MemberExternalId = resource.Name,
                    ObservedOn = observedDay,
                });
            }

            AddCostCenterMetric(batch, costCenter, "ai_credit_pool_current_amount", costCenter.AiCreditPoolState?.CurrentAmount, observedDay);
            AddCostCenterMetric(batch, costCenter, "ai_credit_pool_target_amount", costCenter.AiCreditPoolState?.TargetAmount, observedDay);
            if (costCenter.AiCreditPoolState is { CurrentAmount: { } current, TargetAmount: > 0m } pool)
            {
                AddCostCenterMetric(
                    batch,
                    costCenter,
                    "ai_credit_pool_utilization_percent",
                    decimal.Round(current / pool.TargetAmount!.Value * 100m, 4, MidpointRounding.AwayFromZero),
                    observedDay);
            }
        }
    }

    private async Task SaveBillingExportsAsync(
        BudgetSynchronizationBatch batch,
        CancellationToken cancellationToken)
    {
        foreach (var export in batch.UsageReportExports)
        {
            var record = await dbContext.BillingReportExports.FindAsync(
                [batch.EnterpriseId, export.Id],
                cancellationToken);
            if (record is null)
            {
                record = new BillingReportExportRecord
                {
                    EnterpriseId = batch.EnterpriseId,
                    ReportId = export.Id,
                    ReportType = export.ReportType,
                    Status = export.Status,
                    FirstObservedAt = batch.ObservedAt,
                };
                dbContext.BillingReportExports.Add(record);
            }

            record.ReportType = export.ReportType;
            record.StartDate = export.StartDate;
            record.EndDate = export.EndDate;
            record.Status = export.Status;
            record.DownloadUrlCount = export.DownloadUrls?.Count ?? 0;
            record.CreatedAt = export.CreatedAt;
            record.Actor = export.Actor;
            record.LastObservedAt = batch.ObservedAt;
        }
    }

    private void AddCostCenterMetric(
        BudgetSynchronizationBatch batch,
        GitHubCostCenter costCenter,
        string metricKey,
        decimal? value,
        DateOnly observedDay)
    {
        if (value is null)
        {
            return;
        }

        dbContext.DailyMetrics.Add(new DailyMetricRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = batch.EnterpriseId,
            ScopeKind = ScopeKind.CostCenter,
            ScopeExternalId = costCenter.Id,
            MetricKey = metricKey,
            MetricValue = value,
            Availability = "available",
            Source = CostCenterMetricSource,
            MetricDay = observedDay,
            IngestedAt = batch.ObservedAt,
        });
    }

    private static bool TryMapResourceScope(string resourceType, out ScopeKind scopeKind)
    {
        var normalized = resourceType.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        scopeKind = normalized switch
        {
            "user" or "users" => ScopeKind.User,
            "organization" or "organizations" or "org" => ScopeKind.Organization,
            "repository" or "repositories" or "repo" => ScopeKind.Repository,
            "team" or "teams" or "enterpriseteam" or "enterpriseteams" => ScopeKind.Team,
            _ => default,
        };
        return normalized is "user" or "users"
            or "organization" or "organizations" or "org"
            or "repository" or "repositories" or "repo"
            or "team" or "teams" or "enterpriseteam" or "enterpriseteams";
    }

    private static string CreateCostCenterMetadata(
        string? existingMetadata,
        GitHubCostCenter costCenter)
    {
        string[] ownerPrincipalNames = [];
        if (!string.IsNullOrWhiteSpace(existingMetadata))
        {
            using var document = JsonDocument.Parse(existingMetadata);
            if (document.RootElement.TryGetProperty("ownerPrincipalNames", out var owners)
                && owners.ValueKind == JsonValueKind.Array)
            {
                ownerPrincipalNames = owners.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item!)
                    .ToArray();
            }
        }

        return JsonSerializer.Serialize(new
        {
            costCenter.State,
            costCenter.AzureSubscription,
            costCenter.AiCreditPoolEnabled,
            costCenter.Resources,
            ownerPrincipalNames,
        }, JsonOptions);
    }
}
