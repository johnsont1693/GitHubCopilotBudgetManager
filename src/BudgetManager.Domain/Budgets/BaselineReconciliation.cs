namespace BudgetManager.Domain.Budgets;

public enum BaselineMode
{
    Disabled = 0,
    ProtectedAutomatic = 1,
    ApprovalOnly = 2,
}

public enum BaselineReconciliationAction
{
    None = 0,
    ResetAutomatically = 1,
    RequestApproval = 2,
    ManualDrift = 3,
}

public sealed record BaselineReconciliationDecision(
    BaselineReconciliationAction Action,
    long CurrentAmount,
    long BaselineAmount,
    string Reason);

public static class BaselineReconciliationEvaluator
{
    public static BaselineReconciliationDecision Evaluate(
        BaselineMode mode,
        long currentAmount,
        long baselineAmount,
        long? lastToolWrittenAmount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentAmount);
        ArgumentOutOfRangeException.ThrowIfNegative(baselineAmount);
        if (lastToolWrittenAmount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lastToolWrittenAmount));
        }

        if (mode == BaselineMode.Disabled)
        {
            return new BaselineReconciliationDecision(
                BaselineReconciliationAction.None,
                currentAmount,
                baselineAmount,
                "Monthly baseline reconciliation is disabled.");
        }

        if (currentAmount == baselineAmount)
        {
            return new BaselineReconciliationDecision(
                BaselineReconciliationAction.None,
                currentAmount,
                baselineAmount,
                "The budget already matches its baseline.");
        }

        if (mode == BaselineMode.ApprovalOnly)
        {
            return new BaselineReconciliationDecision(
                BaselineReconciliationAction.RequestApproval,
                currentAmount,
                baselineAmount,
                "Baseline restoration requires enterprise-admin approval.");
        }

        if (lastToolWrittenAmount != currentAmount)
        {
            return new BaselineReconciliationDecision(
                BaselineReconciliationAction.ManualDrift,
                currentAmount,
                baselineAmount,
                "The current GitHub budget no longer matches the tool's last written amount.");
        }

        return new BaselineReconciliationDecision(
            BaselineReconciliationAction.ResetAutomatically,
            currentAmount,
            baselineAmount,
            "The tool-owned budget change can be restored to baseline.");
    }
}
