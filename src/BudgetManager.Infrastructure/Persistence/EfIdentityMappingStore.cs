using BudgetManager.Application.Identity;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Infrastructure.Persistence;

public sealed class EfIdentityMappingStore(BudgetManagerDbContext dbContext) : IIdentityMappingStore
{
    public async Task<IReadOnlyList<GitHubIdentityCandidate>> GetCandidatesAsync(
        Guid enterpriseId,
        CancellationToken cancellationToken = default)
    {
        var loginsFromEntities = await dbContext.ManagedEntities
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId && item.ScopeKind == ScopeKind.User)
            .Select(item => item.ExternalId)
            .ToListAsync(cancellationToken);
        var loginsFromMetrics = await dbContext.DailyMetrics
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId && item.ScopeKind == ScopeKind.User)
            .Select(item => item.ScopeExternalId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var memberships = await dbContext.EntityMemberships
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId
                && item.ParentScopeKind == ScopeKind.CostCenter
                && item.MemberScopeKind == ScopeKind.User)
            .ToListAsync(cancellationToken);
        var costCenterMemberships = memberships
            .GroupBy(item => item.MemberExternalId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.MaxBy(item => item.ObservedOn)!.ParentExternalId,
                StringComparer.OrdinalIgnoreCase);
        return loginsFromEntities
            .Concat(loginsFromMetrics)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(login => new GitHubIdentityCandidate(
                login,
                costCenterMemberships.GetValueOrDefault(login)))
            .ToArray();
    }

    public async Task SaveAsync(
        Guid enterpriseId,
        IReadOnlyList<IdentityMapping> mappings,
        DateTimeOffset synchronizedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var currentRecords = await dbContext.IdentityMappings
            .Where(item => item.EnterpriseId == enterpriseId)
            .ToListAsync(cancellationToken);
        var existing = currentRecords.ToDictionary(
            item => item.GitHubLogin,
            StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
        {
            if (!existing.TryGetValue(mapping.GitHubLogin, out var record))
            {
                record = new IdentityMappingRecord
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = enterpriseId,
                    GitHubLogin = mapping.GitHubLogin,
                    Source = mapping.Source,
                };
                dbContext.IdentityMappings.Add(record);
            }

            record.EntraObjectId = mapping.EntraObjectId;
            record.UserPrincipalName = mapping.UserPrincipalName;
            record.Department = mapping.Department;
            record.EntraCostCenterCode = mapping.EntraCostCenterCode;
            record.GitHubCostCenterId = mapping.GitHubCostCenterId;
            record.Status = Enum.Parse<IdentityMappingStatus>(mapping.Status, true);
            record.Source = mapping.Source;
            record.UpdatedAt = synchronizedAt;
        }

        dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = enterpriseId,
            EventType = "identity.mappings.synchronized",
            ActorType = "service",
            ActorId = "identity-sync",
            TargetType = "enterprise",
            TargetId = enterpriseId.ToString("N"),
            DataJson = System.Text.Json.JsonSerializer.Serialize(new { mappingCount = mappings.Count }),
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAt = synchronizedAt,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
