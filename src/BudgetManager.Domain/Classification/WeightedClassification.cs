namespace BudgetManager.Domain.Classification;

public enum MetricDirection
{
    HigherIsBetter = 0,
    LowerIsBetter = 1,
}

public sealed record WeightedMetricRule
{
    public WeightedMetricRule(
        string metricKey,
        decimal weight,
        decimal poorThreshold,
        decimal goodThreshold,
        MetricDirection direction,
        int maximumAgeDays,
        bool isRequired = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metricKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weight);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumAgeDays);

        if (direction == MetricDirection.HigherIsBetter && goodThreshold <= poorThreshold)
        {
            throw new ArgumentException("Good threshold must be greater than poor threshold for a higher-is-better metric.");
        }

        if (direction == MetricDirection.LowerIsBetter && goodThreshold >= poorThreshold)
        {
            throw new ArgumentException("Good threshold must be less than poor threshold for a lower-is-better metric.");
        }

        MetricKey = metricKey;
        Weight = weight;
        PoorThreshold = poorThreshold;
        GoodThreshold = goodThreshold;
        Direction = direction;
        MaximumAgeDays = maximumAgeDays;
        IsRequired = isRequired;
    }

    public string MetricKey { get; }

    public decimal Weight { get; }

    public decimal PoorThreshold { get; }

    public decimal GoodThreshold { get; }

    public MetricDirection Direction { get; }

    public int MaximumAgeDays { get; }

    public bool IsRequired { get; }
}

public sealed class WeightedClassificationPolicy
{
    public WeightedClassificationPolicy(
        IEnumerable<WeightedMetricRule> metricRules,
        decimal yellowMinimumScore,
        decimal greenMinimumScore)
    {
        ArgumentNullException.ThrowIfNull(metricRules);
        ArgumentOutOfRangeException.ThrowIfNegative(yellowMinimumScore);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(yellowMinimumScore, 100m);
        ArgumentOutOfRangeException.ThrowIfNegative(greenMinimumScore);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(greenMinimumScore, 100m);

        if (greenMinimumScore <= yellowMinimumScore)
        {
            throw new ArgumentException("Green minimum score must be greater than yellow minimum score.");
        }

        var rules = metricRules.ToArray();
        if (rules.Length == 0)
        {
            throw new ArgumentException("At least one metric rule is required.", nameof(metricRules));
        }

        if (rules.Select(rule => rule.MetricKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rules.Length)
        {
            throw new ArgumentException("Metric rule keys must be unique.", nameof(metricRules));
        }

        MetricRules = Array.AsReadOnly(rules);
        YellowMinimumScore = yellowMinimumScore;
        GreenMinimumScore = greenMinimumScore;
    }

    public IReadOnlyList<WeightedMetricRule> MetricRules { get; }

    public decimal YellowMinimumScore { get; }

    public decimal GreenMinimumScore { get; }
}

public static class WeightedClassifier
{
    public static ClassificationResult Evaluate(
        WeightedClassificationPolicy policy,
        IEnumerable<MetricObservation> observations,
        DateOnly evaluatedOn)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(observations);

        var observationMap = CreateObservationMap(observations);
        var reasons = new List<ClassificationReason>();
        var hasUnavailableRequiredMetric = false;
        var totalWeight = 0m;
        var weightedScore = 0m;

        foreach (var rule in policy.MetricRules)
        {
            if (!TryGetUsableObservation(
                    rule.MetricKey,
                    rule.MaximumAgeDays,
                    observationMap,
                    evaluatedOn,
                    out var observation,
                    out var unavailableReason))
            {
                reasons.Add(unavailableReason);
                hasUnavailableRequiredMetric |= rule.IsRequired;
                continue;
            }

            var metricScore = Normalize(observation.Value!.Value, rule);
            weightedScore += metricScore * rule.Weight;
            totalWeight += rule.Weight;
            reasons.Add(new ClassificationReason(
                "metric.scored",
                $"{rule.MetricKey} contributed {metricScore:0.##} points with weight {rule.Weight:0.##}.",
                rule.MetricKey));
        }

        if (hasUnavailableRequiredMetric || totalWeight == 0m)
        {
            return new ClassificationResult(HealthStatus.Unknown, null, reasons.AsReadOnly());
        }

        var score = decimal.Round(weightedScore / totalWeight, 2, MidpointRounding.AwayFromZero);
        var status = score >= policy.GreenMinimumScore
            ? HealthStatus.Green
            : score >= policy.YellowMinimumScore
                ? HealthStatus.Yellow
                : HealthStatus.Red;

        return new ClassificationResult(status, score, reasons.AsReadOnly());
    }

    internal static Dictionary<string, MetricObservation> CreateObservationMap(
        IEnumerable<MetricObservation> observations)
    {
        var observationMap = new Dictionary<string, MetricObservation>(StringComparer.OrdinalIgnoreCase);
        foreach (var observation in observations)
        {
            if (!observationMap.TryAdd(observation.MetricKey, observation))
            {
                throw new ArgumentException(
                    $"Only one observation is allowed for metric '{observation.MetricKey}'.",
                    nameof(observations));
            }
        }

        return observationMap;
    }

    internal static bool TryGetUsableObservation(
        string metricKey,
        int maximumAgeDays,
        IReadOnlyDictionary<string, MetricObservation> observations,
        DateOnly evaluatedOn,
        out MetricObservation observation,
        out ClassificationReason reason)
    {
        if (!observations.TryGetValue(metricKey, out observation!))
        {
            reason = new ClassificationReason("metric.missing", "The metric was not reported.", metricKey);
            return false;
        }

        if (observation.Availability != MetricAvailability.Available)
        {
            reason = new ClassificationReason(
                observation.Availability == MetricAvailability.Suppressed
                    ? "metric.suppressed"
                    : "metric.missing",
                observation.AvailabilityDetail ?? "The metric is unavailable.",
                metricKey);
            return false;
        }

        var ageDays = evaluatedOn.DayNumber - observation.ObservedOn.DayNumber;
        if (ageDays < 0)
        {
            reason = new ClassificationReason("metric.future", "The metric is dated after the evaluation day.", metricKey);
            return false;
        }

        if (ageDays > maximumAgeDays)
        {
            reason = new ClassificationReason(
                "metric.stale",
                $"The metric is {ageDays} days old; the maximum is {maximumAgeDays}.",
                metricKey);
            return false;
        }

        reason = null!;
        return true;
    }

    private static decimal Normalize(decimal value, WeightedMetricRule rule)
    {
        if (rule.Direction == MetricDirection.HigherIsBetter)
        {
            if (value <= rule.PoorThreshold)
            {
                return 0m;
            }

            if (value >= rule.GoodThreshold)
            {
                return 100m;
            }

            return (value - rule.PoorThreshold) / (rule.GoodThreshold - rule.PoorThreshold) * 100m;
        }

        if (value >= rule.PoorThreshold)
        {
            return 0m;
        }

        if (value <= rule.GoodThreshold)
        {
            return 100m;
        }

        return (rule.PoorThreshold - value) / (rule.PoorThreshold - rule.GoodThreshold) * 100m;
    }
}
