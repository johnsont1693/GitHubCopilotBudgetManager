namespace BudgetManager.Application.GitHub;

public sealed record GitHubBudgetAlerting(bool WillAlert, IReadOnlyList<string> AlertRecipients);

public sealed record GitHubBudget(
    string Id,
    string BudgetType,
    long BudgetAmount,
    bool PreventFurtherUsage,
    string BudgetScope,
    string? BudgetEntityName,
    string? User,
    decimal ConsumedAmount,
    string BudgetProductSku,
    GitHubBudgetAlerting BudgetAlerting);

public sealed record GitHubEffectiveBudget(string Id, long BudgetAmount, decimal ConsumedAmount);

public sealed record GitHubBudgetPage(
    IReadOnlyList<GitHubBudget> Budgets,
    string? User,
    GitHubEffectiveBudget? EffectiveBudget,
    bool HasNextPage,
    int TotalCount);

public sealed record GitHubBudgetUpdateResult(string Message, GitHubBudget Budget);
