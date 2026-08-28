namespace BudgetManager.Domain.Budgets;

public sealed record DailySpendObservation
{
    public DailySpendObservation(DateOnly day, decimal amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Day = day;
        Amount = amount;
    }

    public DateOnly Day { get; }

    public decimal Amount { get; }
}

public sealed record BurnRateForecastResult(
    bool IsUsable,
    decimal MonthToDateSpend,
    decimal? ProjectedSpend,
    decimal? MonthToDateDailyAverage,
    decimal? RecentDailyAverage,
    bool WeekdaySeasonalityApplied,
    IReadOnlyList<DateOnly> MissingDays,
    IReadOnlyList<string> Reasons);

public static class BurnRateForecaster
{
    public static BurnRateForecastResult Forecast(
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly asOfDay,
        IEnumerable<DailySpendObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (periodEnd < periodStart)
        {
            throw new ArgumentException("Period end must not precede period start.", nameof(periodEnd));
        }

        if (asOfDay < periodStart || asOfDay > periodEnd)
        {
            throw new ArgumentOutOfRangeException(nameof(asOfDay));
        }

        var observationMap = new Dictionary<DateOnly, decimal>();
        foreach (var observation in observations)
        {
            if (observation.Day > asOfDay)
            {
                continue;
            }

            if (!observationMap.TryAdd(observation.Day, observation.Amount))
            {
                throw new ArgumentException(
                    $"Only one spend observation is allowed for {observation.Day:yyyy-MM-dd}.",
                    nameof(observations));
            }
        }

        var elapsedDays = EnumerateDays(periodStart, asOfDay).ToArray();
        var missingDays = elapsedDays.Where(day => !observationMap.ContainsKey(day)).ToArray();
        var monthToDateSpend = elapsedDays
            .Where(observationMap.ContainsKey)
            .Sum(day => observationMap[day]);
        if (missingDays.Length > 0)
        {
            return new BurnRateForecastResult(
                false,
                monthToDateSpend,
                null,
                null,
                null,
                false,
                missingDays,
                ["The current billing period has missing daily spend observations."]);
        }

        var monthToDateAverage = monthToDateSpend / elapsedDays.Length;
        var recentStart = asOfDay.AddDays(-Math.Min(6, elapsedDays.Length - 1));
        var recentDays = EnumerateDays(recentStart, asOfDay).ToArray();
        var recentAverage = recentDays.Average(day => observationMap[day]);
        var blendedDailyRate = recentAverage * 0.6m + monthToDateAverage * 0.4m;
        var historicalObservations = observationMap
            .Where(item => item.Key < periodStart)
            .ToArray();
        var weekdayFactors = CreateWeekdayFactors(historicalObservations);
        var seasonalityApplied = weekdayFactors is not null;
        var projectedRemaining = 0m;

        foreach (var remainingDay in EnumerateDays(asOfDay.AddDays(1), periodEnd))
        {
            var factor = weekdayFactors?.GetValueOrDefault(remainingDay.DayOfWeek, 1m) ?? 1m;
            projectedRemaining += blendedDailyRate * factor;
        }

        var projectedSpend = decimal.Round(
            monthToDateSpend + projectedRemaining,
            2,
            MidpointRounding.AwayFromZero);
        var reasons = new List<string>
        {
            "Projection blends 60% recent daily velocity with 40% billing-period average.",
        };
        reasons.Add(seasonalityApplied
            ? "Weekday seasonality was applied from at least 28 historical observations."
            : "Weekday seasonality was not applied because sufficient history was unavailable.");

        return new BurnRateForecastResult(
            true,
            monthToDateSpend,
            projectedSpend,
            decimal.Round(monthToDateAverage, 4, MidpointRounding.AwayFromZero),
            decimal.Round(recentAverage, 4, MidpointRounding.AwayFromZero),
            seasonalityApplied,
            [],
            reasons.AsReadOnly());
    }

    private static Dictionary<DayOfWeek, decimal>? CreateWeekdayFactors(
        KeyValuePair<DateOnly, decimal>[] history)
    {
        if (history.Length < 28 || history.Select(item => item.Key.DayOfWeek).Distinct().Count() < 7)
        {
            return null;
        }

        var overallAverage = history.Average(item => item.Value);
        if (overallAverage == 0m)
        {
            return null;
        }

        return history
            .GroupBy(item => item.Key.DayOfWeek)
            .ToDictionary(
                group => group.Key,
                group => group.Average(item => item.Value) / overallAverage);
    }

    private static IEnumerable<DateOnly> EnumerateDays(DateOnly start, DateOnly end)
    {
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            yield return day;
        }
    }
}
