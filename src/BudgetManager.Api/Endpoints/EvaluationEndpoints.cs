using BudgetManager.Api.Contracts;
using BudgetManager.Domain.Budgets;
using BudgetManager.Domain.Classification;

namespace BudgetManager.Api.Endpoints;

public static class EvaluationEndpoints
{
    public static RouteGroupBuilder MapEvaluationEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/classifications/weighted", EvaluateWeighted);
        group.MapPost("/classifications/precedence", EvaluatePrecedence);
        group.MapPost("/budget-increases", EvaluateBudgetIncrease);
        return group;
    }

    private static IResult EvaluateWeighted(WeightedClassificationEvaluationRequest request)
    {
        try
        {
            var policy = new WeightedClassificationPolicy(
                request.Policy.MetricRules.Select(Map),
                request.Policy.YellowMinimumScore,
                request.Policy.GreenMinimumScore);
            var result = WeightedClassifier.Evaluate(
                policy,
                request.Observations.Select(Map),
                request.EvaluatedOn);

            return Results.Ok(ClassificationEvaluationResponse.FromDomain(result));
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
    }

    private static IResult EvaluatePrecedence(PrecedenceClassificationEvaluationRequest request)
    {
        try
        {
            var policy = new PrecedenceClassificationPolicy(
                request.Policy.RequiredMetrics.Select(item =>
                    new MetricRequirement(item.MetricKey, item.MaximumAgeDays)),
                request.Policy.Rules.Select(item => new PrecedenceRule(
                    item.Id,
                    item.MetricKey,
                    item.Comparison,
                    item.Threshold,
                    item.Result,
                    item.MaximumAgeDays,
                    item.Description)),
                request.Policy.DefaultStatus);
            var result = PrecedenceClassifier.Evaluate(
                policy,
                request.Observations.Select(Map),
                request.EvaluatedOn);

            return Results.Ok(ClassificationEvaluationResponse.FromDomain(result));
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
    }

    private static IResult EvaluateBudgetIncrease(BudgetIncreaseEvaluationRequest request)
    {
        try
        {
            var policy = new BudgetIncreaseGuardrailPolicy(
                request.Policy.MaximumIncreaseAmount,
                request.Policy.MaximumIncreasePercent,
                request.Policy.MaximumCumulativeMonthlyIncrease,
                request.Policy.ForecastHeadroomPercent,
                TimeSpan.FromHours(request.Policy.CooldownHours),
                TimeSpan.FromHours(request.Policy.MaximumDataAgeHours));
            var proposal = new BudgetIncreaseRequest(
                request.Proposal.BudgetId,
                request.Proposal.CurrentAmount,
                request.Proposal.ProposedAmount,
                request.Proposal.ForecastAmount,
                request.Proposal.CumulativeMonthlyIncrease,
                request.Proposal.DataAsOf,
                request.Proposal.DataFingerprint,
                request.Proposal.LastAppliedAt,
                request.Proposal.LastAppliedFingerprint);
            var result = BudgetIncreaseGuardrailEvaluator.Evaluate(policy, proposal, request.EvaluatedAt);

            return Results.Ok(BudgetIncreaseEvaluationResponse.FromDomain(result));
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
    }

    private static WeightedMetricRule Map(WeightedMetricRuleRequest item) => new(
        item.MetricKey,
        item.Weight,
        item.PoorThreshold,
        item.GoodThreshold,
        item.Direction,
        item.MaximumAgeDays,
        item.IsRequired);

    private static MetricObservation Map(MetricObservationRequest item) => new(
        item.MetricKey,
        item.Value,
        item.ObservedOn,
        item.Availability,
        item.AvailabilityDetail);

    private static IResult InvalidRequest(ArgumentException exception) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Invalid evaluation request",
        detail: exception.Message);
}
