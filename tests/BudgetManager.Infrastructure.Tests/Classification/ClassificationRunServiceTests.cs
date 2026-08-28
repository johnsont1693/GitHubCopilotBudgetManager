using BudgetManager.Application.Classification;
using BudgetManager.Domain.Classification;

namespace BudgetManager.Infrastructure.Tests.Classification;

public sealed class ClassificationRunServiceTests
{
    [Fact]
    public async Task RunAsync_evaluates_stored_weighted_policy_and_persists_explanation()
    {
        var policyId = Guid.NewGuid();
        var store = new RecordingStore(
            new StoredPolicy(policyId, 1, "Weighted", "Enforced", "Enterprise", "acme", """
                {"metricRules":[{"metricKey":"active_users","weight":1,"poorThreshold":10,"goodThreshold":20,"direction":"HigherIsBetter","maximumAgeDays":3}],"yellowMinimumScore":40,"greenMinimumScore":70}
                """),
            new ClassificationTarget("Enterprise", "acme", [new MetricObservation("active_users", 20m, new DateOnly(2026, 8, 26))]));
        var service = new ClassificationRunService(store, new FixedTimeProvider(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero)));

        var result = await service.RunAsync(Guid.NewGuid());

        Assert.Equal(1, result.EvaluationCount);
        Assert.Equal(HealthStatus.Green, Assert.Single(store.Snapshots!).Result.Status);
        Assert.NotEmpty(Assert.Single(store.Snapshots!).InputsFingerprint);
    }

    private sealed class RecordingStore(StoredPolicy policy, ClassificationTarget target) : IClassificationStore
    {
        public IReadOnlyList<ClassificationSnapshot>? Snapshots { get; private set; }

        public Task<IReadOnlyList<StoredPolicy>> GetActivePoliciesAsync(Guid enterpriseId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StoredPolicy>>([policy]);

        public Task<IReadOnlyList<ClassificationTarget>> GetTargetsAsync(Guid enterpriseId, StoredPolicy storedPolicy, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ClassificationTarget>>([target]);

        public Task SaveAsync(Guid enterpriseId, IReadOnlyList<ClassificationSnapshot> snapshots, CancellationToken cancellationToken = default)
        {
            Snapshots = snapshots;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
