using System.Text.Json;
using BudgetManager.Application.Budgets;
using BudgetManager.Domain.Budgets;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Infrastructure.Persistence;

public sealed class EfBudgetForecastStore(BudgetManagerDbContext dbContext) : IBudgetForecastStore
{
    public async Task<IReadOnlyList<BudgetForecastInput>> GetInputsAsync(
        Guid enterpriseId,
        DateOnly asOfDay,
        CancellationToken cancellationToken = default)
    {
        var periodStart = new DateOnly(asOfDay.Year, asOfDay.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        var periodStartTime = new DateTimeOffset(periodStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var periodEndTime = new DateTimeOffset(asOfDay.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var history = await dbContext.BudgetSnapshots
            .AsNoTracking()
            .Where(item => item.EnterpriseId == enterpriseId
                && item.ObservedAt >= periodStartTime
                && item.ObservedAt < periodEndTime)
            .ToListAsync(cancellationToken);
        var result = new List<BudgetForecastInput>();
        foreach (var budgetHistory in history.GroupBy(item => item.BudgetId, StringComparer.Ordinal))
        {
            var dailyTotals = budgetHistory
                .GroupBy(item => DateOnly.FromDateTime(item.ObservedAt.UtcDateTime))
                .Select(group => group.MaxBy(item => item.ObservedAt)!)
                .OrderBy(item => item.ObservedAt)
                .ToArray();
            var dailySpend = new List<DailySpendObservation>(dailyTotals.Length);
            decimal previousConsumed = 0m;
            foreach (var dailyTotal in dailyTotals)
            {
                var amount = Math.Max(0m, dailyTotal.ConsumedAmount - previousConsumed);
                dailySpend.Add(new DailySpendObservation(
                    DateOnly.FromDateTime(dailyTotal.ObservedAt.UtcDateTime),
                    amount));
                previousConsumed = dailyTotal.ConsumedAmount;
            }

            var latest = dailyTotals[^1];
            result.Add(new BudgetForecastInput(
                latest.BudgetId,
                latest.BudgetAmount,
                periodStart,
                periodEnd,
                asOfDay,
                dailySpend.AsReadOnly()));
        }

        return result.AsReadOnly();
    }

    public async Task SaveAsync(
        Guid enterpriseId,
        IReadOnlyList<BudgetForecastSnapshot> snapshots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var fingerprints = snapshots.Select(item => item.InputsFingerprint).ToArray();
        var existing = await dbContext.ForecastSnapshots
            .AsNoTracking()
            .Where(item => fingerprints.Contains(item.InputsFingerprint))
            .Select(item => item.InputsFingerprint)
            .ToListAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.Ordinal);
        foreach (var snapshot in snapshots.Where(item => !existingSet.Contains(item.InputsFingerprint)))
        {
            var periodStart = snapshot.Forecast.MissingDays.Count > 0
                ? new DateOnly(snapshot.Forecast.MissingDays[0].Year, snapshot.Forecast.MissingDays[0].Month, 1)
                : new DateOnly(snapshot.EvaluatedAt.Year, snapshot.EvaluatedAt.Month, 1);
            dbContext.ForecastSnapshots.Add(new ForecastSnapshotRecord
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                BudgetId = snapshot.BudgetId,
                PeriodStart = periodStart,
                PeriodEnd = periodStart.AddMonths(1).AddDays(-1),
                AsOfDay = DateOnly.FromDateTime(snapshot.EvaluatedAt.UtcDateTime),
                BudgetAmount = snapshot.BudgetAmount,
                MonthToDateSpend = snapshot.Forecast.MonthToDateSpend,
                ProjectedSpend = snapshot.Forecast.ProjectedSpend,
                UtilizationPercent = snapshot.UtilizationPercent,
                IsUsable = snapshot.Forecast.IsUsable,
                ComponentsJson = JsonSerializer.Serialize(snapshot.Forecast),
                InputsFingerprint = snapshot.InputsFingerprint,
                EvaluatedAt = snapshot.EvaluatedAt,
            });
        }

        dbContext.AuditEvents.Add(new AuditEventRecord
        {
            Id = Guid.NewGuid(),
            EnterpriseId = enterpriseId,
            EventType = "forecasts.evaluated",
            ActorType = "service",
            ActorId = "forecast-runner",
            TargetType = "enterprise",
            TargetId = enterpriseId.ToString("N"),
            DataJson = JsonSerializer.Serialize(new { forecastCount = snapshots.Count }),
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAt = snapshots.Count > 0 ? snapshots[0].EvaluatedAt : DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
