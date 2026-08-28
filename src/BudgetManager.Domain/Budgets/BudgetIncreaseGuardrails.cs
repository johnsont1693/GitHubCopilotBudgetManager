namespace BudgetManager.Domain.Budgets;

public sealed record BudgetIncreaseGuardrailPolicy
{
    public BudgetIncreaseGuardrailPolicy(
        long maximumIncreaseAmount,
        decimal maximumIncreasePercent,
        long maximumCumulativeMonthlyIncrease,
        decimal forecastHeadroomPercent,
        TimeSpan cooldown,
        TimeSpan maximumDataAge)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumIncreaseAmount);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumIncreasePercent);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCumulativeMonthlyIncrease);
        ArgumentOutOfRangeException.ThrowIfNegative(forecastHeadroomPercent);
        ArgumentOutOfRangeException.ThrowIfLessThan(cooldown, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDataAge, TimeSpan.Zero);

        MaximumIncreaseAmount = maximumIncreaseAmount;
        MaximumIncreasePercent = maximumIncreasePercent;
        MaximumCumulativeMonthlyIncrease = maximumCumulativeMonthlyIncrease;
        ForecastHeadroomPercent = forecastHeadroomPercent;
        Cooldown = cooldown;
        MaximumDataAge = maximumDataAge;
    }

    public long MaximumIncreaseAmount { get; }

    public decimal MaximumIncreasePercent { get; }

    public long MaximumCumulativeMonthlyIncrease { get; }

    public decimal ForecastHeadroomPercent { get; }

    public TimeSpan Cooldown { get; }

    public TimeSpan MaximumDataAge { get; }
}

public sealed record BudgetIncreaseRequest
{
    public BudgetIncreaseRequest(
        string budgetId,
        long currentAmount,
        long proposedAmount,
        decimal forecastAmount,
        long cumulativeMonthlyIncrease,
        DateTimeOffset dataAsOf,
        string dataFingerprint,
        DateTimeOffset? lastAppliedAt = null,
        string? lastAppliedFingerprint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(budgetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFingerprint);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(currentAmount);
        ArgumentOutOfRangeException.ThrowIfNegative(proposedAmount);
        ArgumentOutOfRangeException.ThrowIfNegative(forecastAmount);
        ArgumentOutOfRangeException.ThrowIfNegative(cumulativeMonthlyIncrease);

        BudgetId = budgetId;
        CurrentAmount = currentAmount;
        ProposedAmount = proposedAmount;
        ForecastAmount = forecastAmount;
        CumulativeMonthlyIncrease = cumulativeMonthlyIncrease;
        DataAsOf = dataAsOf;
        DataFingerprint = dataFingerprint;
        LastAppliedAt = lastAppliedAt;
        LastAppliedFingerprint = lastAppliedFingerprint;
    }

    public string BudgetId { get; }

    public long CurrentAmount { get; }

    public long ProposedAmount { get; }

    public decimal ForecastAmount { get; }

    public long CumulativeMonthlyIncrease { get; }

    public DateTimeOffset DataAsOf { get; }

    public string DataFingerprint { get; }

    public DateTimeOffset? LastAppliedAt { get; }

    public string? LastAppliedFingerprint { get; }
}

public sealed record BudgetGuardrailViolation(string Code, string Message);

public sealed record BudgetIncreaseDecision(
    bool IsAllowed,
    long MaximumPermittedAmount,
    IReadOnlyList<BudgetGuardrailViolation> Violations);

public static class BudgetIncreaseGuardrailEvaluator
{
    public static BudgetIncreaseDecision Evaluate(
        BudgetIncreaseGuardrailPolicy policy,
        BudgetIncreaseRequest request,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(request);

        var violations = new List<BudgetGuardrailViolation>();
        var maximumByAmount = request.CurrentAmount + policy.MaximumIncreaseAmount;
        var maximumByPercent = request.CurrentAmount
            + (long)decimal.Floor(request.CurrentAmount * policy.MaximumIncreasePercent / 100m);
        var remainingMonthlyIncrease = Math.Max(
            0,
            policy.MaximumCumulativeMonthlyIncrease - request.CumulativeMonthlyIncrease);
        var maximumByMonth = request.CurrentAmount + remainingMonthlyIncrease;
        var maximumByForecast = (long)decimal.Floor(
            request.ForecastAmount * (1m + policy.ForecastHeadroomPercent / 100m));
        var maximumPermittedAmount = new[]
        {
            maximumByAmount,
            maximumByPercent,
            maximumByMonth,
            maximumByForecast,
        }.Min();

        if (request.ProposedAmount <= request.CurrentAmount)
        {
            violations.Add(new BudgetGuardrailViolation(
                "proposal.not_increase",
                "The proposed amount must be greater than the current budget."));
        }

        var dataAge = evaluatedAt - request.DataAsOf;
        if (dataAge < TimeSpan.Zero)
        {
            violations.Add(new BudgetGuardrailViolation(
                "data.future",
                "The financial data is dated after the evaluation time."));
        }
        else if (dataAge > policy.MaximumDataAge)
        {
            violations.Add(new BudgetGuardrailViolation(
                "data.stale",
                $"The financial data is older than {policy.MaximumDataAge}."));
        }

        if (string.Equals(
                request.DataFingerprint,
                request.LastAppliedFingerprint,
                StringComparison.Ordinal))
        {
            violations.Add(new BudgetGuardrailViolation(
                "request.duplicate",
                "This data fingerprint has already produced an applied increase."));
        }

        if (request.LastAppliedAt is { } lastAppliedAt
            && evaluatedAt - lastAppliedAt < policy.Cooldown)
        {
            violations.Add(new BudgetGuardrailViolation(
                "request.cooldown",
                "The budget is still within its increase cooldown period."));
        }

        if (request.ProposedAmount > maximumByAmount)
        {
            violations.Add(new BudgetGuardrailViolation(
                "cap.absolute",
                $"The proposal exceeds the per-change cap of {policy.MaximumIncreaseAmount}."));
        }

        if (request.ProposedAmount > maximumByPercent)
        {
            violations.Add(new BudgetGuardrailViolation(
                "cap.percent",
                $"The proposal exceeds the per-change cap of {policy.MaximumIncreasePercent:0.##}%."));
        }

        if (request.ProposedAmount > maximumByMonth)
        {
            violations.Add(new BudgetGuardrailViolation(
                "cap.monthly",
                "The proposal exceeds the remaining cumulative monthly increase allowance."));
        }

        if (request.ProposedAmount > maximumByForecast)
        {
            violations.Add(new BudgetGuardrailViolation(
                "cap.forecast",
                "The proposal exceeds the forecast plus configured headroom."));
        }

        return new BudgetIncreaseDecision(
            violations.Count == 0,
            maximumPermittedAmount,
            violations.AsReadOnly());
    }
}
