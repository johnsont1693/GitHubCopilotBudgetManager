namespace BudgetManager.Infrastructure.Persistence;

public sealed class EnterpriseRecord
{
    public Guid Id { get; set; }

    public required string Slug { get; set; }

    public required string DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ManagedEntityRecord
{
    public Guid Id { get; set; }

    public Guid EnterpriseId { get; set; }

    public ScopeKind ScopeKind { get; set; }

    public required string ExternalId { get; set; }

    public required string DisplayName { get; set; }

    public ScopeKind? ParentScopeKind { get; set; }

    public string? ParentExternalId { get; set; }

    public string? MetadataJson { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset ObservedAt { get; set; }
}

public sealed class EntityMembershipRecord
{
    public Guid Id { get; set; }

    public Guid EnterpriseId { get; set; }

    public ScopeKind ParentScopeKind { get; set; }

    public required string ParentExternalId { get; set; }

    public ScopeKind MemberScopeKind { get; set; }

    public required string MemberExternalId { get; set; }

    public DateOnly ObservedOn { get; set; }
}

public sealed class IdentityMappingRecord
{
    public Guid Id { get; set; }

    public Guid EnterpriseId { get; set; }

    public required string GitHubLogin { get; set; }

    public string? EntraObjectId { get; set; }

    public string? UserPrincipalName { get; set; }

    public string? Department { get; set; }

    public string? EntraCostCenterCode { get; set; }

    public string? GitHubCostCenterId { get; set; }

    public IdentityMappingStatus Status { get; set; }

    public required string Source { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class RetentionPolicyRecord
{
    public Guid EnterpriseId { get; set; }

    public int RawReportDays { get; set; }

    public int UserMetricDays { get; set; }

    public int AggregateMetricDays { get; set; }

    public int NotificationDays { get; set; }

    public int AuditDays { get; set; }

    public bool LegalHold { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
