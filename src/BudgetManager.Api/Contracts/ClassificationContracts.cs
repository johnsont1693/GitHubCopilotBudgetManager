using BudgetManager.Domain.Classification;

namespace BudgetManager.Api.Contracts;

public sealed record MetricObservationRequest(
    string MetricKey,
    decimal? Value,
    DateOnly ObservedOn,
    MetricAvailability Availability = MetricAvailability.Available,
    string? AvailabilityDetail = null);

public sealed record WeightedMetricRuleRequest(
    string MetricKey,
    decimal Weight,
    decimal PoorThreshold,
    decimal GoodThreshold,
    MetricDirection Direction,
    int MaximumAgeDays,
    bool IsRequired = true);

public sealed record WeightedPolicyRequest(
    IReadOnlyList<WeightedMetricRuleRequest> MetricRules,
    decimal YellowMinimumScore,
    decimal GreenMinimumScore);

public sealed record WeightedClassificationEvaluationRequest(
    WeightedPolicyRequest Policy,
    IReadOnlyList<MetricObservationRequest> Observations,
    DateOnly EvaluatedOn);

public sealed record MetricRequirementRequest(string MetricKey, int MaximumAgeDays);

public sealed record PrecedenceRuleRequest(
    string Id,
    string MetricKey,
    MetricComparison Comparison,
    decimal Threshold,
    HealthStatus Result,
    int MaximumAgeDays,
    string Description);

public sealed record PrecedencePolicyRequest(
    IReadOnlyList<MetricRequirementRequest> RequiredMetrics,
    IReadOnlyList<PrecedenceRuleRequest> Rules,
    HealthStatus DefaultStatus);

public sealed record PrecedenceClassificationEvaluationRequest(
    PrecedencePolicyRequest Policy,
    IReadOnlyList<MetricObservationRequest> Observations,
    DateOnly EvaluatedOn);

public sealed record ClassificationReasonResponse(string Code, string Message, string? MetricKey);

public sealed record ClassificationEvaluationResponse(
    HealthStatus Status,
    decimal? Score,
    IReadOnlyList<ClassificationReasonResponse> Reasons)
{
    public static ClassificationEvaluationResponse FromDomain(ClassificationResult result) => new(
        result.Status,
        result.Score,
        result.Reasons
            .Select(reason => new ClassificationReasonResponse(reason.Code, reason.Message, reason.MetricKey))
            .ToArray());
}
