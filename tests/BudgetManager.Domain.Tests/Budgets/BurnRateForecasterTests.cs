using BudgetManager.Domain.Budgets;

namespace BudgetManager.Domain.Tests.Budgets;

public sealed class BurnRateForecasterTests
{
    [Fact]
    public void Forecast_projects_with_explainable_blended_velocity()
    {
        var periodStart = new DateOnly(2026, 8, 1);
        var asOf = new DateOnly(2026, 8, 10);
        var observations = Enumerable.Range(0, 10)
            .Select(offset => new DailySpendObservation(periodStart.AddDays(offset), 10m))
            .ToArray();

        var result = BurnRateForecaster.Forecast(
            periodStart,
            new DateOnly(2026, 8, 31),
            asOf,
            observations);

        Assert.True(result.IsUsable);
        Assert.Equal(100m, result.MonthToDateSpend);
        Assert.Equal(310m, result.ProjectedSpend);
        Assert.Equal(10m, result.MonthToDateDailyAverage);
        Assert.Equal(10m, result.RecentDailyAverage);
        Assert.False(result.WeekdaySeasonalityApplied);
    }

    [Fact]
    public void Forecast_is_unusable_when_current_period_days_are_missing()
    {
        var result = BurnRateForecaster.Forecast(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            new DateOnly(2026, 8, 3),
            [
                new DailySpendObservation(new DateOnly(2026, 8, 1), 10m),
                new DailySpendObservation(new DateOnly(2026, 8, 3), 10m),
            ]);

        Assert.False(result.IsUsable);
        Assert.Null(result.ProjectedSpend);
        Assert.Equal(new DateOnly(2026, 8, 2), Assert.Single(result.MissingDays));
    }
}
