using BudgetManager.Application.GitHub;

namespace BudgetManager.Application.Budgets;

public sealed class BudgetSynchronizationService
{
    private const int PageSize = 100;
    private const int MaximumPages = 1_000;

    private readonly IGitHubBudgetClient budgetClient;
    private readonly IGitHubEnterpriseClient enterpriseClient;
    private readonly IBudgetSnapshotStore snapshotStore;
    private readonly TimeProvider timeProvider;

    public BudgetSynchronizationService(
        IGitHubBudgetClient budgetClient,
        IGitHubEnterpriseClient enterpriseClient,
        IBudgetSnapshotStore snapshotStore,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(budgetClient);
        ArgumentNullException.ThrowIfNull(enterpriseClient);
        ArgumentNullException.ThrowIfNull(snapshotStore);

        this.budgetClient = budgetClient;
        this.enterpriseClient = enterpriseClient;
        this.snapshotStore = snapshotStore;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<BudgetSynchronizationResult> SynchronizeAsync(
        Guid enterpriseId,
        string enterpriseSlug,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(enterpriseId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(enterpriseSlug);

        var budgets = new Dictionary<string, GitHubBudget>(StringComparer.Ordinal);
        GitHubEffectiveBudget? effectiveBudget = null;
        var pageNumber = 1;

        while (true)
        {
            if (pageNumber > MaximumPages)
            {
                throw new InvalidOperationException(
                    $"Budget synchronization exceeded the {MaximumPages} page safety limit.");
            }

            var page = await budgetClient.GetBudgetsAsync(
                enterpriseSlug,
                pageNumber,
                PageSize,
                cancellationToken);

            foreach (var budget in page.Budgets)
            {
                if (!budgets.TryAdd(budget.Id, budget))
                {
                    throw new InvalidOperationException(
                        $"GitHub returned duplicate budget ID '{budget.Id}' during synchronization.");
                }
            }

            if (page.EffectiveBudget is not null)
            {
                if (effectiveBudget is not null && effectiveBudget.Id != page.EffectiveBudget.Id)
                {
                    throw new InvalidOperationException(
                        "GitHub returned conflicting effective budgets across pages.");
                }

                effectiveBudget = page.EffectiveBudget;
            }

            if (!page.HasNextPage)
            {
                break;
            }

            pageNumber++;
        }

        var costCenters = await enterpriseClient.GetCostCentersAsync(
            enterpriseSlug,
            cancellationToken: cancellationToken);
        var usageReportExports = await enterpriseClient.GetUsageReportExportsAsync(
            enterpriseSlug,
            cancellationToken);
        var userStates = new List<BudgetUserStateObservation>();
        foreach (var budget in budgets.Values)
        {
            var userStatePageNumber = 1;
            while (true)
            {
                if (userStatePageNumber > MaximumPages)
                {
                    throw new InvalidOperationException(
                        $"Budget user-state synchronization for '{budget.Id}' exceeded the {MaximumPages} page safety limit.");
                }

                var userStatePage = await enterpriseClient.GetBudgetUserStatesAsync(
                    enterpriseSlug,
                    budget.Id,
                    userStatePageNumber,
                    PageSize,
                    cancellationToken);
                userStates.AddRange(userStatePage.UserStates.Select(item =>
                    new BudgetUserStateObservation(budget.Id, item)));
                if (!userStatePage.HasNextPage)
                {
                    break;
                }

                userStatePageNumber++;
            }
        }

        var observedAt = timeProvider.GetUtcNow();
        var batch = new BudgetSynchronizationBatch(
            enterpriseId,
            enterpriseSlug,
            observedAt,
            budgets.Values.ToArray(),
            effectiveBudget,
            costCenters.CostCenters,
            userStates.AsReadOnly(),
            usageReportExports.UsageReportExports);
        await snapshotStore.SaveAsync(batch, cancellationToken);

        return new BudgetSynchronizationResult(
            budgets.Count,
            pageNumber,
            effectiveBudget?.Id,
            costCenters.CostCenters.Count,
            userStates.Count,
            usageReportExports.UsageReportExports.Count,
            observedAt);
    }
}
