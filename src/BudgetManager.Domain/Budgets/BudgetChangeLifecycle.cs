namespace BudgetManager.Domain.Budgets;

public enum BudgetChangeLifecycleStatus
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

public sealed class BudgetChangeLifecycle
{
    public BudgetChangeLifecycle(
        Guid id,
        string budgetId,
        long expectedCurrentAmount,
        long proposedAmount,
        string dataFingerprint,
        bool automaticMode,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(budgetId);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedCurrentAmount);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(proposedAmount, expectedCurrentAmount);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFingerprint);

        if (expiresAt <= createdAt)
        {
            throw new ArgumentException("Expiration must be after creation.", nameof(expiresAt));
        }

        Id = id;
        BudgetId = budgetId;
        ExpectedCurrentAmount = expectedCurrentAmount;
        ProposedAmount = proposedAmount;
        DataFingerprint = dataFingerprint;
        AutomaticMode = automaticMode;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Status = BudgetChangeLifecycleStatus.PendingApproval;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid Id { get; }

    public string BudgetId { get; }

    public long ExpectedCurrentAmount { get; }

    public long ProposedAmount { get; }

    public string DataFingerprint { get; }

    public bool AutomaticMode { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public BudgetChangeLifecycleStatus Status { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    public string? DecisionActor { get; private set; }

    public DateTimeOffset? DecisionAt { get; private set; }

    public string? FailureCode { get; private set; }

    public void Approve(string actorObjectId, DateTimeOffset decidedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorObjectId);
        EnsurePendingAndCurrent(decidedAt);
        Status = BudgetChangeLifecycleStatus.Approved;
        DecisionActor = actorObjectId;
        DecisionAt = decidedAt;
        AdvanceVersion();
    }

    public void AuthorizeAutomatic(DateTimeOffset authorizedAt)
    {
        if (!AutomaticMode)
        {
            throw new InvalidOperationException("The request is not configured for automatic execution.");
        }

        EnsurePendingAndCurrent(authorizedAt);
        Status = BudgetChangeLifecycleStatus.Approved;
        DecisionActor = "automation";
        DecisionAt = authorizedAt;
        AdvanceVersion();
    }

    public void Reject(string actorObjectId, DateTimeOffset decidedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorObjectId);
        EnsurePendingAndCurrent(decidedAt);
        Status = BudgetChangeLifecycleStatus.Rejected;
        DecisionActor = actorObjectId;
        DecisionAt = decidedAt;
        AdvanceVersion();
    }

    public void BeginExecution()
    {
        EnsureStatus(BudgetChangeLifecycleStatus.Approved);
        Status = BudgetChangeLifecycleStatus.Executing;
        AdvanceVersion();
    }

    public void MarkApplied()
    {
        EnsureStatus(BudgetChangeLifecycleStatus.Executing);
        Status = BudgetChangeLifecycleStatus.Applied;
        AdvanceVersion();
    }

    public void MarkConflict(string failureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        EnsureStatus(BudgetChangeLifecycleStatus.Executing);
        Status = BudgetChangeLifecycleStatus.Conflict;
        FailureCode = failureCode;
        AdvanceVersion();
    }

    public void MarkFailed(string failureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        EnsureStatus(BudgetChangeLifecycleStatus.Executing);
        Status = BudgetChangeLifecycleStatus.Failed;
        FailureCode = failureCode;
        AdvanceVersion();
    }

    public void Expire(DateTimeOffset checkedAt)
    {
        EnsureStatus(BudgetChangeLifecycleStatus.PendingApproval);
        if (checkedAt < ExpiresAt)
        {
            throw new InvalidOperationException("The request has not reached its expiration time.");
        }

        Status = BudgetChangeLifecycleStatus.Expired;
        AdvanceVersion();
    }

    private void EnsurePendingAndCurrent(DateTimeOffset decidedAt)
    {
        EnsureStatus(BudgetChangeLifecycleStatus.PendingApproval);
        if (decidedAt >= ExpiresAt)
        {
            Status = BudgetChangeLifecycleStatus.Expired;
            AdvanceVersion();
            throw new InvalidOperationException("The budget change request has expired.");
        }
    }

    private void EnsureStatus(BudgetChangeLifecycleStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException(
                $"Budget change request must be {expected} but is {Status}.");
        }
    }

    private void AdvanceVersion() => ConcurrencyToken = Guid.NewGuid();
}
