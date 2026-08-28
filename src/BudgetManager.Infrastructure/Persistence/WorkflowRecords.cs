namespace BudgetManager.Infrastructure.Persistence;

public sealed class BudgetSnapshotRecord
{
    public Guid Id { get; set; }

    public Guid EnterpriseId { get; set; }

    public required string BudgetId { get; set; }

    public required string BudgetType { get; set; }

    public required string BudgetScope { get; set; }

    public string? BudgetEntityName { get; set; }

    public string? UserLogin { get; set; }

    public long BudgetAmount { get; set; }

    public decimal ConsumedAmount { get; set; }

    public required string ProductSku { get; set; }

    public bool PreventFurtherUsage { get; set; }

    public required string AlertingJson { get; set; }

    public bool IsEffective { get; set; }

    public DateTimeOffset ObservedAt { get; set; }
}

public sealed class BudgetChangeRequestRecord
{
    public Guid Id { get; set; }

    public Guid EnterpriseId { get; set; }

    public required string BudgetId { get; set; }

    public long ExpectedCurrentAmount { get; set; }

    public long ProposedAmount { get; set; }

    public decimal ForecastAmount { get; set; }

    public required string DataFingerprint { get; set; }

    public required string EvidenceJson { get; set; }

    public BudgetChangeStatus Status { get; set; }

    public bool AutomaticMode { get; set; }

    public Guid ConcurrencyToken { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ExecutedAt { get; set; }

    public string? FailureCode { get; set; }
}

public sealed class ApprovalDecisionRecord
{
    public Guid Id { get; set; }

    public Guid BudgetChangeRequestId { get; set; }

    public required string Decision { get; set; }

    public required string ActorObjectId { get; set; }

    public string? Reason { get; set; }

    public required string CallbackNonceHash { get; set; }

    public DateTimeOffset DecidedAt { get; set; }
}

public sealed class BudgetBaselineRecord
{
    public Guid EnterpriseId { get; set; }

    public required string BudgetId { get; set; }

    public long BaselineAmount { get; set; }

    public BaselineReconciliationMode Mode { get; set; }

    public long? LastToolWrittenAmount { get; set; }

    public DateTimeOffset? LastReconciledAt { get; set; }

    public required string UpdatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class NotificationDeliveryRecord
{
    public Guid Id { get; set; }

    public Guid EnterpriseId { get; set; }

    public required string EventFingerprint { get; set; }

    public required string Channel { get; set; }

    public required string RecipientKey { get; set; }

    public required string TemplateVersion { get; set; }

    public DeliveryStatus Status { get; set; }

    public int AttemptCount { get; set; }

    public string? ProviderMessageId { get; set; }

    public string? LastErrorCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DeliveredAt { get; set; }
}

public sealed class OutboxMessageRecord
{
    public Guid Id { get; set; }

    public Guid EnterpriseId { get; set; }

    public required string EventType { get; set; }

    public required string EventFingerprint { get; set; }

    public required string PayloadJson { get; set; }

    public OutboxStatus Status { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public string? LastErrorCode { get; set; }
}

public sealed class AuditEventRecord
{
    public Guid Id { get; set; }

    public Guid EnterpriseId { get; set; }

    public required string EventType { get; set; }

    public required string ActorType { get; set; }

    public required string ActorId { get; set; }

    public required string TargetType { get; set; }

    public required string TargetId { get; set; }

    public required string DataJson { get; set; }

    public required string CorrelationId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
