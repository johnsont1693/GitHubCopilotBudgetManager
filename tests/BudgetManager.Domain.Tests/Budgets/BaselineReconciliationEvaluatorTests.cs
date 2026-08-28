using BudgetManager.Domain.Budgets;

namespace BudgetManager.Domain.Tests.Budgets;

public sealed class BaselineReconciliationEvaluatorTests
{
    [Theory]
    [InlineData(BaselineMode.Disabled, 1_250, 1_000, 1_250, BaselineReconciliationAction.None)]
    [InlineData(BaselineMode.ProtectedAutomatic, 1_000, 1_000, 1_250, BaselineReconciliationAction.None)]
    [InlineData(BaselineMode.ApprovalOnly, 1_250, 1_000, 1_250, BaselineReconciliationAction.RequestApproval)]
    [InlineData(BaselineMode.ProtectedAutomatic, 1_300, 1_000, 1_250, BaselineReconciliationAction.ManualDrift)]
    [InlineData(BaselineMode.ProtectedAutomatic, 1_250, 1_000, 1_250, BaselineReconciliationAction.ResetAutomatically)]
    public void Evaluate_selects_the_configured_safe_action(
        BaselineMode mode,
        long currentAmount,
        long baselineAmount,
        long lastToolWrittenAmount,
        BaselineReconciliationAction expected)
    {
        var result = BaselineReconciliationEvaluator.Evaluate(
            mode,
            currentAmount,
            baselineAmount,
            lastToolWrittenAmount);

        Assert.Equal(expected, result.Action);
    }
}
