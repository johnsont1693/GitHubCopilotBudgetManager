using System.Security.Cryptography;
using System.Text;
using BudgetManager.Domain.Budgets;

namespace BudgetManager.Application.Budgets;

public sealed record BudgetForecastInput(
    string BudgetId,
    long BudgetAmount,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly AsOfDay,
    IReadOnlyList<DailySpendObservation> DailySpend);

public sealed record BudgetForecastSnapshot(
    string BudgetId,
    long BudgetAmount,
    BurnRateForecastResult Forecast,
    decimal? UtilizationPercent,
    string InputsFingerprint,
    DateTimeOffset EvaluatedAt);

public sealed record BudgetForecastRunResult(int BudgetCount, int UsableCount, int UnusableCount);

public interface IBudgetForecastStore
{
    Task<IReadOnlyList<BudgetForecastInput>> GetInputsAsync(
        Guid enterpriseId,
        DateOnly asOfDay,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        Guid enterpriseId,
        IReadOnlyList<BudgetForecastSnapshot> snapshots,
        CancellationToken cancellationToken = default);
}

public sealed class BudgetForecastService
{
    private readonly IBudgetForecastStore store;
    private readonly TimeProvider timeProvider;

    public BudgetForecastService(IBudgetForecastStore store, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<BudgetForecastRunResult> RunAsync(
        Guid enterpriseId,
        DateOnly? asOfDay = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(enterpriseId, Guid.Empty);
        var evaluatedAt = timeProvider.GetUtcNow();
        var effectiveAsOfDay = asOfDay ?? DateOnly.FromDateTime(evaluatedAt.UtcDateTime);
        var inputs = await store.GetInputsAsync(enterpriseId, effectiveAsOfDay, cancellationToken);
        var snapshots = inputs.Select(input =>
        {
            var forecast = BurnRateForecaster.Forecast(
                input.PeriodStart,
                input.PeriodEnd,
                input.AsOfDay,
                input.DailySpend);
            decimal? utilization = forecast.ProjectedSpend is not null && input.BudgetAmount > 0
                ? decimal.Round(forecast.ProjectedSpend.Value / input.BudgetAmount * 100m, 2)
                : null;
            return new BudgetForecastSnapshot(
                input.BudgetId,
                input.BudgetAmount,
                forecast,
                utilization,
                CreateFingerprint(input),
                evaluatedAt);
        }).ToArray();
        await store.SaveAsync(enterpriseId, snapshots, cancellationToken);
        return new BudgetForecastRunResult(
            snapshots.Length,
            snapshots.Count(item => item.Forecast.IsUsable),
            snapshots.Count(item => !item.Forecast.IsUsable));
    }

    private static string CreateFingerprint(BudgetForecastInput input)
    {
        var value = new StringBuilder()
            .Append(input.BudgetId).Append('|')
            .Append(input.BudgetAmount).Append('|')
            .Append(input.AsOfDay);
        foreach (var observation in input.DailySpend.OrderBy(item => item.Day))
        {
            value.Append('|').Append(observation.Day).Append('=').Append(observation.Amount);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
    }
}
