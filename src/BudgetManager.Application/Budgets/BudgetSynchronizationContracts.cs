using BudgetManager.Application.GitHub;

namespace BudgetManager.Application.Budgets;

public sealed record BudgetSynchronizationBatch(
    Guid EnterpriseId,
    string EnterpriseSlug,
    DateTimeOffset ObservedAt,
    IReadOnlyList<GitHubBudget> Budgets,
    GitHubEffectiveBudget? EffectiveBudget,
    IReadOnlyList<GitHubCostCenter> CostCenters,
    IReadOnlyList<BudgetUserStateObservation> UserStates,
    IReadOnlyList<GitHubUsageReportExport> UsageReportExports);

public sealed record BudgetUserStateObservation(
    string BudgetId,
    GitHubBudgetUserState State);

public sealed record BudgetSynchronizationResult(
    int BudgetCount,
    int PageCount,
    string? EffectiveBudgetId,
    int CostCenterCount,
    int UserStateCount,
    int UsageReportExportCount,
    DateTimeOffset ObservedAt);

public interface IBudgetSnapshotStore
{
    Task SaveAsync(
        BudgetSynchronizationBatch batch,
        CancellationToken cancellationToken = default);
}
