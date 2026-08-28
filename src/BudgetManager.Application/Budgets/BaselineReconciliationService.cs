using BudgetManager.Domain.Budgets;

namespace BudgetManager.Application.Budgets;

public sealed record BaselineReconciliationItem(
    Guid EnterpriseId,
    string BudgetId,
    long CurrentAmount,
    long BaselineAmount,
    long? LastToolWrittenAmount,
    BaselineMode Mode);

public sealed record BaselineReconciliationResult(
    int Evaluated,
    int AutomaticRequests,
    int ApprovalRequests,
    int ManualDrift,
    int NoAction);

public interface IBaselineReconciliationStore
{
    Task<IReadOnlyList<BaselineReconciliationItem>> GetItemsAsync(Guid enterpriseId, CancellationToken cancellationToken = default);

    Task SaveDecisionsAsync(
        Guid enterpriseId,
        IReadOnlyList<(BaselineReconciliationItem Item, BaselineReconciliationDecision Decision)> decisions,
        DateTimeOffset evaluatedAt,
        bool dryRun,
        CancellationToken cancellationToken = default);
}

public sealed class BaselineReconciliationService
{
    private readonly IBaselineReconciliationStore store;
    private readonly TimeProvider timeProvider;

    public BaselineReconciliationService(
        IBaselineReconciliationStore store,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<BaselineReconciliationResult> ReconcileAsync(
        Guid enterpriseId,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(enterpriseId, Guid.Empty);
        var items = await store.GetItemsAsync(enterpriseId, cancellationToken);
        var decisions = items.Select(item => (
            Item: item,
            Decision: BaselineReconciliationEvaluator.Evaluate(
                item.Mode,
                item.CurrentAmount,
                item.BaselineAmount,
                item.LastToolWrittenAmount))).ToArray();
        await store.SaveDecisionsAsync(
            enterpriseId,
            decisions,
            timeProvider.GetUtcNow(),
            dryRun,
            cancellationToken);
        return new BaselineReconciliationResult(
            decisions.Length,
            decisions.Count(item => item.Decision.Action == BaselineReconciliationAction.ResetAutomatically),
            decisions.Count(item => item.Decision.Action == BaselineReconciliationAction.RequestApproval),
            decisions.Count(item => item.Decision.Action == BaselineReconciliationAction.ManualDrift),
            decisions.Count(item => item.Decision.Action == BaselineReconciliationAction.None));
    }
}
