namespace BudgetManager.Application.GitHub;

public interface IGitHubBudgetClient
{
    Task<GitHubBudgetPage> GetBudgetsAsync(
        string enterpriseSlug,
        int page = 1,
        int perPage = 100,
        CancellationToken cancellationToken = default);

    Task<GitHubBudget> GetBudgetAsync(
        string enterpriseSlug,
        string budgetId,
        CancellationToken cancellationToken = default);

    Task<GitHubBudgetUpdateResult> UpdateBudgetAmountAsync(
        string enterpriseSlug,
        string budgetId,
        long budgetAmount,
        CancellationToken cancellationToken = default);
}
