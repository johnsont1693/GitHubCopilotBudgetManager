using System.Text.Json;
using BudgetManager.Application.Classification;
using BudgetManager.Domain.Classification;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Infrastructure.Persistence;

public sealed class EfClassificationStore(BudgetManagerDbContext dbContext) : IClassificationStore
{
    public async Task<IReadOnlyList<StoredPolicy>> GetActivePoliciesAsync(Guid enterpriseId, CancellationToken cancellationToken = default) => await dbContext.Policies
        .AsNoTracking()
        .Where(item => item.EnterpriseId == enterpriseId && item.IsActive)
        .Select(item => new StoredPolicy(item.Id, item.Version, item.Mode.ToString(), item.Governance.ToString(), item.ScopeKind.ToString(), item.ScopeExternalId, item.DefinitionJson))
        .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ClassificationTarget>> GetTargetsAsync(Guid enterpriseId, StoredPolicy policy, CancellationToken cancellationToken = default)
    {
        var targetKeys = new List<(ScopeKind ScopeKind, string ScopeExternalId)>
        {
            (Enum.Parse<ScopeKind>(policy.ScopeKind, true), policy.ScopeExternalId),
        };
        if (policy.ScopeKind.Equals("Enterprise", StringComparison.OrdinalIgnoreCase)
            && policy.Governance.Equals("Enforced", StringComparison.OrdinalIgnoreCase))
        {
            var entities = await dbContext.ManagedEntities
                .AsNoTracking()
                .Where(item => item.EnterpriseId == enterpriseId && item.IsActive)
                .Select(item => new { item.ScopeKind, item.ExternalId })
                .ToListAsync(cancellationToken);
            targetKeys.AddRange(entities.Select(item => (item.ScopeKind, item.ExternalId)));
        }

        var targets = new List<ClassificationTarget>();
        foreach (var targetKey in targetKeys.Distinct())
        {
            var metricRows = await dbContext.DailyMetrics
                .AsNoTracking()
                .Where(item => item.EnterpriseId == enterpriseId
                    && item.ScopeKind == targetKey.ScopeKind
                    && item.ScopeExternalId == targetKey.ScopeExternalId)
                .ToListAsync(cancellationToken);
            var observations = metricRows
                .GroupBy(item => item.MetricKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.MaxBy(item => item.MetricDay)!)
                .Select(item => new MetricObservation(
                    item.MetricKey,
                    item.MetricValue,
                    item.MetricDay,
                    Enum.Parse<MetricAvailability>(item.Availability, true),
                    item.AvailabilityDetail))
                .ToArray();
            targets.Add(new ClassificationTarget(targetKey.ScopeKind.ToString(), targetKey.ScopeExternalId, observations));
        }

        return targets.AsReadOnly();
    }

    public async Task SaveAsync(Guid enterpriseId, IReadOnlyList<ClassificationSnapshot> snapshots, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var fingerprints = snapshots.Select(item => item.InputsFingerprint).ToArray();
        var existing = await dbContext.ClassificationSnapshots
            .AsNoTracking()
            .Where(item => fingerprints.Contains(item.InputsFingerprint))
            .Select(item => new { item.PolicyId, item.InputsFingerprint })
            .ToListAsync(cancellationToken);
        var existingKeys = existing.Select(item => $"{item.PolicyId:N}:{item.InputsFingerprint}").ToHashSet(StringComparer.Ordinal);
        foreach (var snapshot in snapshots)
        {
            if (existingKeys.Contains($"{snapshot.PolicyId:N}:{snapshot.InputsFingerprint}"))
            {
                continue;
            }

            dbContext.ClassificationSnapshots.Add(new ClassificationSnapshotRecord
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                ScopeKind = Enum.Parse<ScopeKind>(snapshot.ScopeKind, true),
                ScopeExternalId = snapshot.ScopeExternalId,
                PolicyId = snapshot.PolicyId,
                PolicyVersion = snapshot.PolicyVersion,
                Status = snapshot.Result.Status.ToString().ToLowerInvariant(),
                Score = snapshot.Result.Score,
                ReasonsJson = JsonSerializer.Serialize(snapshot.Result.Reasons),
                InputsFingerprint = snapshot.InputsFingerprint,
                EvaluatedAt = snapshot.EvaluatedAt,
            });
        }

        dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = enterpriseId,
            EventType = "classifications.evaluated",
            ActorType = "service",
            ActorId = "classification-runner",
            TargetType = "enterprise",
            TargetId = enterpriseId.ToString("N"),
            DataJson = JsonSerializer.Serialize(new { evaluationCount = snapshots.Count }),
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAt = snapshots.Count > 0 ? snapshots[0].EvaluatedAt : DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
