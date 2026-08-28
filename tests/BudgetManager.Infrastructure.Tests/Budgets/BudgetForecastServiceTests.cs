using BudgetManager.Application.Budgets;
using BudgetManager.Domain.Budgets;

namespace BudgetManager.Infrastructure.Tests.Budgets;

public sealed class BudgetForecastServiceTests
{
    [Fact]
    public async Task RunAsync_persists_projected_utilization_and_components()
    {
        var start = new DateOnly(2026, 8, 1);
        var store = new RecordingStore(new BudgetForecastInput(
            "budget-1", 400, start, new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 10),
            Enumerable.Range(0, 10).Select(offset => new DailySpendObservation(start.AddDays(offset), 10)).ToArray()));
        var service = new BudgetForecastService(
            store,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero)));

        var result = await service.RunAsync(Guid.NewGuid(), new DateOnly(2026, 8, 10));

        Assert.Equal(1, result.UsableCount);
        var snapshot = Assert.Single(store.Snapshots!);
        Assert.Equal(310m, snapshot.Forecast.ProjectedSpend);
        Assert.Equal(77.5m, snapshot.UtilizationPercent);
    }

    private sealed class RecordingStore(BudgetForecastInput input) : IBudgetForecastStore
    {
        public IReadOnlyList<BudgetForecastSnapshot>? Snapshots { get; private set; }
        public Task<IReadOnlyList<BudgetForecastInput>> GetInputsAsync(Guid enterpriseId, DateOnly asOfDay, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<BudgetForecastInput>>([input]);
        public Task SaveAsync(Guid enterpriseId, IReadOnlyList<BudgetForecastSnapshot> snapshots, CancellationToken cancellationToken = default) { Snapshots = snapshots; return Task.CompletedTask; }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
