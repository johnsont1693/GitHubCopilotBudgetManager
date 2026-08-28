using BudgetManager.Domain.Classification;

namespace BudgetManager.Domain.Tests.Classification;

public sealed class WeightedClassifierTests
{
    private static readonly DateOnly EvaluationDay = new(2026, 8, 26);

    [Fact]
    public void Evaluate_combines_higher_and_lower_is_better_metrics()
    {
        var policy = new WeightedClassificationPolicy(
        [
            new WeightedMetricRule("active_seat_percent", 3m, 60m, 80m, MetricDirection.HigherIsBetter, 3),
            new WeightedMetricRule("budget_consumed_percent", 2m, 100m, 70m, MetricDirection.LowerIsBetter, 1),
        ],
        yellowMinimumScore: 40m,
        greenMinimumScore: 70m);
        MetricObservation[] observations =
        [
            new("active_seat_percent", 70m, EvaluationDay),
            new("budget_consumed_percent", 85m, EvaluationDay),
        ];

        var result = WeightedClassifier.Evaluate(policy, observations, EvaluationDay);

        Assert.Equal(HealthStatus.Yellow, result.Status);
        Assert.Equal(50m, result.Score);
        Assert.Equal(2, result.Reasons.Count);
    }

    [Fact]
    public void Evaluate_returns_unknown_when_a_required_metric_is_suppressed()
    {
        var policy = new WeightedClassificationPolicy(
        [
            new WeightedMetricRule("team_adoption", 1m, 40m, 80m, MetricDirection.HigherIsBetter, 3),
        ],
        yellowMinimumScore: 40m,
        greenMinimumScore: 70m);
        MetricObservation[] observations =
        [
            new(
                "team_adoption",
                null,
                EvaluationDay,
                MetricAvailability.Suppressed,
                "GitHub suppresses teams with fewer than five seated users."),
        ];

        var result = WeightedClassifier.Evaluate(policy, observations, EvaluationDay);

        Assert.Equal(HealthStatus.Unknown, result.Status);
        Assert.Null(result.Score);
        Assert.Contains(result.Reasons, reason => reason.Code == "metric.suppressed");
    }

    [Fact]
    public void Evaluate_returns_unknown_when_a_required_metric_is_stale()
    {
        var policy = new WeightedClassificationPolicy(
        [
            new WeightedMetricRule("active_users", 1m, 10m, 20m, MetricDirection.HigherIsBetter, 2),
        ],
        yellowMinimumScore: 40m,
        greenMinimumScore: 70m);
        MetricObservation[] observations =
        [
            new("active_users", 20m, EvaluationDay.AddDays(-3)),
        ];

        var result = WeightedClassifier.Evaluate(policy, observations, EvaluationDay);

        Assert.Equal(HealthStatus.Unknown, result.Status);
        Assert.Contains(result.Reasons, reason => reason.Code == "metric.stale");
    }
}
