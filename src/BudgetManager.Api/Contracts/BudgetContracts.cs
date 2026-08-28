using BudgetManager.Domain.Budgets;

namespace BudgetManager.Api.Contracts;

public sealed record BudgetIncreaseGuardrailPolicyRequest(
    long MaximumIncreaseAmount,
    decimal MaximumIncreasePercent,
    long MaximumCumulativeMonthlyIncrease,
    decimal ForecastHeadroomPercent,
    double CooldownHours,
    double MaximumDataAgeHours);

public sealed record BudgetIncreaseRequestContract(
    string BudgetId,
    long CurrentAmount,
    long ProposedAmount,
    decimal ForecastAmount,
    long CumulativeMonthlyIncrease,
    DateTimeOffset DataAsOf,
    string DataFingerprint,
    DateTimeOffset? LastAppliedAt = null,
    string? LastAppliedFingerprint = null);

public sealed record BudgetIncreaseEvaluationRequest(
    BudgetIncreaseGuardrailPolicyRequest Policy,
    BudgetIncreaseRequestContract Proposal,
    DateTimeOffset EvaluatedAt);

public sealed record BudgetGuardrailViolationResponse(string Code, string Message);

public sealed record BudgetIncreaseEvaluationResponse(
    bool IsAllowed,
    long MaximumPermittedAmount,
    IReadOnlyList<BudgetGuardrailViolationResponse> Violations)
{
    public static BudgetIncreaseEvaluationResponse FromDomain(BudgetIncreaseDecision decision) => new(
        decision.IsAllowed,
        decision.MaximumPermittedAmount,
        decision.Violations
            .Select(violation => new BudgetGuardrailViolationResponse(violation.Code, violation.Message))
            .ToArray());
}

public sealed record BudgetChangeProposalRequest(string BudgetId, long ProposedAmount);

public sealed record CreateBudgetChangeRequest(
    Guid EnterpriseId,
    BudgetChangeProposalRequest Proposal,
    bool AutomaticMode);

public sealed record BudgetChangeApiOptions(TimeSpan ApprovalLifetime);

public sealed record BudgetDecisionRequest(Guid ExpectedConcurrencyToken, string? Reason = null);
