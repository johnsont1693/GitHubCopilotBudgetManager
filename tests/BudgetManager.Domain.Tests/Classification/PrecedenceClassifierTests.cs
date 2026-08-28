using BudgetManager.Domain.Classification;

namespace BudgetManager.Domain.Tests.Classification;

public sealed class PrecedenceClassifierTests
{
    private static readonly DateOnly EvaluationDay = new(2026, 8, 26);

    [Theory]
    [InlineData(105, HealthStatus.Red, "Spend reached the hard-stop threshold.")]
    [InlineData(90, HealthStatus.Yellow, "Spend reached the warning threshold.")]
    [InlineData(50, HealthStatus.Green, "No precedence rule matched; the default status is Green.")]
    public void Evaluate_uses_the_first_matching_rule(
        decimal consumedPercent,
        HealthStatus expectedStatus,
        string expectedReason)
    {
        var policy = new PrecedenceClassificationPolicy(
        [
            new MetricRequirement("budget_consumed_percent", 1),
        ],
        [
            new PrecedenceRule(
                "hard-stop",
                "budget_consumed_percent",
                MetricComparison.GreaterThanOrEqual,
                100m,
                HealthStatus.Red,
                1,
                "Spend reached the hard-stop threshold."),
            new PrecedenceRule(
                "warning",
                "budget_consumed_percent",
                MetricComparison.GreaterThanOrEqual,
                80m,
                HealthStatus.Yellow,
                1,
                "Spend reached the warning threshold."),
        ],
        HealthStatus.Green);
        MetricObservation[] observations =
        [
            new("budget_consumed_percent", consumedPercent, EvaluationDay),
        ];

        var result = PrecedenceClassifier.Evaluate(policy, observations, EvaluationDay);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedReason, Assert.Single(result.Reasons).Message);
    }

    [Fact]
    public void Evaluate_returns_unknown_before_rules_when_required_data_is_missing()
    {
        var policy = new PrecedenceClassificationPolicy(
        [
            new MetricRequirement("budget_consumed_percent", 1),
        ],
        [],
        HealthStatus.Green);

        var result = PrecedenceClassifier.Evaluate(policy, [], EvaluationDay);

        Assert.Equal(HealthStatus.Unknown, result.Status);
        Assert.Contains(result.Reasons, reason => reason.Code == "metric.missing");
    }
}
