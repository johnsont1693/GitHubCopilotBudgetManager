namespace BudgetManager.Infrastructure.Persistence;

public sealed class DailyMetricRecord
{
    public Guid Id { get; set; }

    public Guid EnterpriseId { get; set; }

    public ScopeKind ScopeKind { get; set; }

    public required string ScopeExternalId { get; set; }

    public required string MetricKey { get; set; }

    public decimal? MetricValue { get; set; }

    public required string Availability { get; set; }

    public string? AvailabilityDetail { get; set; }

    public required string Source { get; set; }

    public DateOnly MetricDay { get; set; }

    public DateTimeOffset IngestedAt { get; set; }
}

public sealed class PolicyRecord
{
    public Guid Id { get; set; }

    public Guid EnterpriseId { get; set; }

    public required string Name { get; set; }

    public int Version { get; set; }

    public ClassificationPolicyMode Mode { get; set; }

    public PolicyGovernance Governance { get; set; }

    public ScopeKind ScopeKind { get; set; }

    public required string ScopeExternalId { get; set; }

    public required string DefinitionJson { get; set; }

    public bool IsActive { get; set; }

    public required string CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ClassificationSnapshotRecord
{
    public Guid Id { get; set; }

    public Guid EnterpriseId { get; set; }

    public ScopeKind ScopeKind { get; set; }

    public required string ScopeExternalId { get; set; }

    public Guid PolicyId { get; set; }

    public int PolicyVersion { get; set; }

    public required string Status { get; set; }

    public decimal? Score { get; set; }

    public required string ReasonsJson { get; set; }

    public required string InputsFingerprint { get; set; }

    public DateTimeOffset EvaluatedAt { get; set; }
}

public sealed class IngestionManifestRecord
{
    public Guid Id { get; set; }

    public Guid EnterpriseId { get; set; }

    public required string ReportType { get; set; }

    public DateOnly ReportStartDay { get; set; }

    public DateOnly ReportEndDay { get; set; }

    public string? BlobName { get; set; }

    public string? SourceUrlHash { get; set; }

    public string? ContentSha256 { get; set; }

    public IngestionStatus Status { get; set; }

    public int RecordCount { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorDetail { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class ForecastSnapshotRecord
{
    public Guid Id { get; set; }

    public Guid EnterpriseId { get; set; }

    public required string BudgetId { get; set; }

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public DateOnly AsOfDay { get; set; }

    public long BudgetAmount { get; set; }

    public decimal MonthToDateSpend { get; set; }

    public decimal? ProjectedSpend { get; set; }

    public decimal? UtilizationPercent { get; set; }

    public bool IsUsable { get; set; }

    public required string ComponentsJson { get; set; }

    public required string InputsFingerprint { get; set; }

    public DateTimeOffset EvaluatedAt { get; set; }
}
