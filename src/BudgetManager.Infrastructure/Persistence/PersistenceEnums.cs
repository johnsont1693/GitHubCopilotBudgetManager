namespace BudgetManager.Infrastructure.Persistence;

public enum ScopeKind
{
    Enterprise = 0,
    Organization = 1,
    CostCenter = 2,
    Team = 3,
    Repository = 4,
    User = 5,
}

public enum ClassificationPolicyMode
{
    Weighted = 0,
    Precedence = 1,
}

public enum PolicyGovernance
{
    Enforced = 0,
    Delegated = 1,
}

public enum IngestionStatus
{
    Pending = 0,
    Processing = 1,
    Succeeded = 2,
    Failed = 3,
}

public enum BudgetChangeStatus
{
    PendingApproval = 0,
    Approved = 1,
    Rejected = 2,
    Executing = 3,
    Applied = 4,
    Conflict = 5,
    Failed = 6,
    Expired = 7,
}

public enum BaselineReconciliationMode
{
    Disabled = 0,
    ProtectedAutomatic = 1,
    ApprovalOnly = 2,
}

public enum IdentityMappingStatus
{
    Unmatched = 0,
    Matched = 1,
    Duplicate = 2,
    Ignored = 3,
}

public enum DeliveryStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    Suppressed = 3,
}

public enum OutboxStatus
{
    Pending = 0,
    Processing = 1,
    Processed = 2,
    Failed = 3,
}
