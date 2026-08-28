using BudgetManager.Domain.Classification;

namespace BudgetManager.Application.Classification;

public sealed record MetricDefinition(
    string Key,
    string DisplayName,
    string Category,
    string Unit,
    MetricDirection Direction,
    IReadOnlyList<string> SupportedScopes,
    string Source,
    string Caveat);

public static class MetricCatalog
{
    public static IReadOnlyList<MetricDefinition> All { get; } =
    [
        new("budget_consumed_percent", "Budget consumed", "Financial", "percent", MetricDirection.LowerIsBetter, ["Enterprise", "Organization", "CostCenter", "User"], "GitHub budgets", "Consumption can lag the underlying activity."),
        new("forecast_utilization_percent", "Forecast utilization", "Financial", "percent", MetricDirection.LowerIsBetter, ["Enterprise", "Organization", "CostCenter"], "Budget Manager forecast", "Projection is directional and depends on complete daily spend."),
        new("active_seat_percent", "Active seats", "License hygiene", "percent", MetricDirection.HigherIsBetter, ["Enterprise", "Organization", "CostCenter", "Team"], "Copilot seats and usage", "Activity can be absent when IDE telemetry is unavailable."),
        new("daily_active_users", "Daily active users", "Adoption", "users", MetricDirection.HigherIsBetter, ["Enterprise", "Organization"], "Copilot metrics", "Do not sum daily distinct-user counts across time windows."),
        new("weekly_active_users", "Weekly active users", "Adoption", "users", MetricDirection.HigherIsBetter, ["Enterprise", "Organization"], "Copilot metrics", "Rolling distinct-user count."),
        new("monthly_active_users", "Monthly active users", "Adoption", "users", MetricDirection.HigherIsBetter, ["Enterprise", "Organization"], "Copilot metrics", "Rolling distinct-user count."),
        new("user_initiated_interaction_count", "User interactions", "Engagement", "interactions", MetricDirection.HigherIsBetter, ["Enterprise", "Organization", "Team", "User"], "Copilot metrics", "Measures engagement, not productivity."),
        new("code_generation_activity_count", "Code generation activities", "Engagement", "activities", MetricDirection.HigherIsBetter, ["Enterprise", "Organization", "Team", "User"], "Copilot metrics", "Coverage depends on supported clients."),
        new("code_acceptance_activity_count", "Code acceptance activities", "Value signals", "activities", MetricDirection.HigherIsBetter, ["Enterprise", "Organization", "Team", "User"], "Copilot metrics", "Acceptance is a directional signal, not code quality."),
        new("loc_added_sum", "Lines added", "Value signals", "lines", MetricDirection.HigherIsBetter, ["Enterprise", "Organization", "Team", "User"], "Copilot metrics", "Lines of code are directional and must not be used as an individual productivity target."),
        new("ai_credits_used", "AI credits used", "Financial", "credits", MetricDirection.LowerIsBetter, ["Enterprise", "Organization", "CostCenter", "User"], "Copilot metrics", "Consumption metric is not the invoiced total."),
        new("ai_credit_pool_current_amount", "AI credit pool consumed", "Financial", "credits", MetricDirection.LowerIsBetter, ["CostCenter"], "GitHub cost centers", "Current pool usage can lag activity."),
        new("ai_credit_pool_target_amount", "AI credit pool target", "Financial", "credits", MetricDirection.HigherIsBetter, ["CostCenter"], "GitHub cost centers", "The target is entitlement-derived and is not an invoice total."),
        new("ai_credit_pool_utilization_percent", "AI credit pool utilization", "Financial", "percent", MetricDirection.LowerIsBetter, ["CostCenter"], "GitHub cost centers", "Utilization depends on GitHub's current and target pool amounts."),
        new("used_agent", "Agent used", "Feature adoption", "boolean", MetricDirection.HigherIsBetter, ["User"], "Copilot metrics", "A binary usage signal does not indicate outcome quality."),
        new("used_chat", "Chat used", "Feature adoption", "boolean", MetricDirection.HigherIsBetter, ["User"], "Copilot metrics", "Some GitHub.com and mobile interactions are not included."),
        new("used_cli", "CLI used", "Feature adoption", "boolean", MetricDirection.HigherIsBetter, ["User"], "Copilot metrics", "CLI activity is tracked separately from IDE active users."),
        new("pull_requests.total_created", "Pull requests created", "Repository activity", "pull requests", MetricDirection.HigherIsBetter, ["Enterprise", "Organization", "Repository"], "Copilot repository metrics", "Pull request activity is associative and not a causal productivity measure."),
        new("pull_requests.total_reviewed", "Pull requests reviewed", "Repository activity", "pull requests", MetricDirection.HigherIsBetter, ["Enterprise", "Organization", "Repository"], "Copilot repository metrics", "A pull request can be counted on multiple review days."),
        new("pull_requests.total_merged", "Pull requests merged", "Repository activity", "pull requests", MetricDirection.HigherIsBetter, ["Enterprise", "Organization", "Repository"], "Copilot repository metrics", "Do not compare repository totals without accounting for repository mix."),
        new("pull_requests.total_copilot_suggestions", "Copilot review suggestions", "Repository activity", "suggestions", MetricDirection.HigherIsBetter, ["Enterprise", "Organization", "Repository"], "Copilot repository metrics", "Suggestion volume does not indicate review quality."),
        new("pull_requests.total_copilot_applied_suggestions", "Applied Copilot review suggestions", "Repository activity", "suggestions", MetricDirection.HigherIsBetter, ["Enterprise", "Organization", "Repository"], "Copilot repository metrics", "Applied suggestions are directional and not a code-quality measure."),
        new("pull_requests.median_minutes_to_merge", "Median minutes to merge", "Repository activity", "minutes", MetricDirection.LowerIsBetter, ["Enterprise", "Repository"], "Copilot repository metrics", "Medians are not additive and are not derived at organization scope."),
    ];
}
