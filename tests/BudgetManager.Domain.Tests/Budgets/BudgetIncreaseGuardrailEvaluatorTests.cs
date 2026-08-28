using BudgetManager.Domain.Budgets;

namespace BudgetManager.Domain.Tests.Budgets;

public sealed class BudgetIncreaseGuardrailEvaluatorTests
{
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly BudgetIncreaseGuardrailPolicy Policy = new(
        maximumIncreaseAmount: 500,
        maximumIncreasePercent: 50m,
        maximumCumulativeMonthlyIncrease: 700,
        forecastHeadroomPercent: 20m,
        cooldown: TimeSpan.FromDays(7),
        maximumDataAge: TimeSpan.FromDays(2));

    [Fact]
    public void Evaluate_allows_a_fresh_proposal_within_every_cap()
    {
        var request = new BudgetIncreaseRequest(
            "budget-1",
            currentAmount: 1_000,
            proposedAmount: 1_200,
            forecastAmount: 1_100m,
            cumulativeMonthlyIncrease: 0,
            dataAsOf: EvaluatedAt.AddHours(-4),
            dataFingerprint: "2026-08-26:budget-1");

        var result = BudgetIncreaseGuardrailEvaluator.Evaluate(Policy, request, EvaluatedAt);

        Assert.True(result.IsAllowed);
        Assert.Empty(result.Violations);
        Assert.Equal(1_320, result.MaximumPermittedAmount);
    }

    [Fact]
    public void Evaluate_reports_every_violated_guardrail()
    {
        var request = new BudgetIncreaseRequest(
            "budget-1",
            currentAmount: 1_000,
            proposedAmount: 1_700,
            forecastAmount: 1_100m,
            cumulativeMonthlyIncrease: 600,
            dataAsOf: EvaluatedAt.AddDays(-3),
            dataFingerprint: "same-window",
            lastAppliedAt: EvaluatedAt.AddDays(-1),
            lastAppliedFingerprint: "same-window");

        var result = BudgetIncreaseGuardrailEvaluator.Evaluate(Policy, request, EvaluatedAt);

        Assert.False(result.IsAllowed);
        Assert.Equal(1_100, result.MaximumPermittedAmount);
        Assert.Contains(result.Violations, item => item.Code == "data.stale");
        Assert.Contains(result.Violations, item => item.Code == "request.duplicate");
        Assert.Contains(result.Violations, item => item.Code == "request.cooldown");
        Assert.Contains(result.Violations, item => item.Code == "cap.absolute");
        Assert.Contains(result.Violations, item => item.Code == "cap.percent");
        Assert.Contains(result.Violations, item => item.Code == "cap.monthly");
        Assert.Contains(result.Violations, item => item.Code == "cap.forecast");
    }
}
