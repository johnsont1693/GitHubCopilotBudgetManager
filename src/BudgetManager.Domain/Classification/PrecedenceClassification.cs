namespace BudgetManager.Domain.Classification;

public enum MetricComparison
{
    LessThan = 0,
    LessThanOrEqual = 1,
    GreaterThan = 2,
    GreaterThanOrEqual = 3,
    Equal = 4,
}

public sealed record MetricRequirement
{
    public MetricRequirement(string metricKey, int maximumAgeDays)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metricKey);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumAgeDays);
        MetricKey = metricKey;
        MaximumAgeDays = maximumAgeDays;
    }

    public string MetricKey { get; }

    public int MaximumAgeDays { get; }
}

public sealed record PrecedenceRule
{
    public PrecedenceRule(
        string id,
        string metricKey,
        MetricComparison comparison,
        decimal threshold,
        HealthStatus result,
        int maximumAgeDays,
        string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(metricKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumAgeDays);

        if (result == HealthStatus.Unknown)
        {
            throw new ArgumentException("A precedence rule cannot assign Unknown.", nameof(result));
        }

        Id = id;
        MetricKey = metricKey;
        Comparison = comparison;
        Threshold = threshold;
        Result = result;
        MaximumAgeDays = maximumAgeDays;
        Description = description;
    }

    public string Id { get; }

    public string MetricKey { get; }

    public MetricComparison Comparison { get; }

    public decimal Threshold { get; }

    public HealthStatus Result { get; }

    public int MaximumAgeDays { get; }

    public string Description { get; }
}

public sealed class PrecedenceClassificationPolicy
{
    public PrecedenceClassificationPolicy(
        IEnumerable<MetricRequirement> requiredMetrics,
        IEnumerable<PrecedenceRule> rules,
        HealthStatus defaultStatus)
    {
        ArgumentNullException.ThrowIfNull(requiredMetrics);
        ArgumentNullException.ThrowIfNull(rules);

        if (defaultStatus == HealthStatus.Unknown)
        {
            throw new ArgumentException("Default status cannot be Unknown.", nameof(defaultStatus));
        }

        var requirements = requiredMetrics.ToArray();
        var orderedRules = rules.ToArray();

        if (requirements.Select(item => item.MetricKey).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != requirements.Length)
        {
            throw new ArgumentException("Required metric keys must be unique.", nameof(requiredMetrics));
        }

        if (orderedRules.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != orderedRules.Length)
        {
            throw new ArgumentException("Precedence rule IDs must be unique.", nameof(rules));
        }

        RequiredMetrics = Array.AsReadOnly(requirements);
        Rules = Array.AsReadOnly(orderedRules);
        DefaultStatus = defaultStatus;
    }

    public IReadOnlyList<MetricRequirement> RequiredMetrics { get; }

    public IReadOnlyList<PrecedenceRule> Rules { get; }

    public HealthStatus DefaultStatus { get; }
}

public static class PrecedenceClassifier
{
    public static ClassificationResult Evaluate(
        PrecedenceClassificationPolicy policy,
        IEnumerable<MetricObservation> observations,
        DateOnly evaluatedOn)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(observations);

        var observationMap = WeightedClassifier.CreateObservationMap(observations);
        var reasons = new List<ClassificationReason>();

        foreach (var requirement in policy.RequiredMetrics)
        {
            if (!WeightedClassifier.TryGetUsableObservation(
                    requirement.MetricKey,
                    requirement.MaximumAgeDays,
                    observationMap,
                    evaluatedOn,
                    out _,
                    out var unavailableReason))
            {
                reasons.Add(unavailableReason);
            }
        }

        if (reasons.Count > 0)
        {
            return new ClassificationResult(HealthStatus.Unknown, null, reasons.AsReadOnly());
        }

        foreach (var rule in policy.Rules)
        {
            if (!WeightedClassifier.TryGetUsableObservation(
                    rule.MetricKey,
                    rule.MaximumAgeDays,
                    observationMap,
                    evaluatedOn,
                    out var observation,
                    out _))
            {
                continue;
            }

            if (Matches(observation.Value!.Value, rule.Comparison, rule.Threshold))
            {
                reasons.Add(new ClassificationReason(
                    "rule.matched",
                    rule.Description,
                    rule.MetricKey));
                return new ClassificationResult(rule.Result, null, reasons.AsReadOnly());
            }
        }

        reasons.Add(new ClassificationReason(
            "rule.default",
            $"No precedence rule matched; the default status is {policy.DefaultStatus}."));
        return new ClassificationResult(policy.DefaultStatus, null, reasons.AsReadOnly());
    }

    private static bool Matches(decimal value, MetricComparison comparison, decimal threshold) => comparison switch
    {
        MetricComparison.LessThan => value < threshold,
        MetricComparison.LessThanOrEqual => value <= threshold,
        MetricComparison.GreaterThan => value > threshold,
        MetricComparison.GreaterThanOrEqual => value >= threshold,
        MetricComparison.Equal => value == threshold,
        _ => throw new ArgumentOutOfRangeException(nameof(comparison)),
    };
}
