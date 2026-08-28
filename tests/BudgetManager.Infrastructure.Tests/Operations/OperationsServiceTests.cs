using BudgetManager.Application.Budgets;
using BudgetManager.Application.Operations;
using BudgetManager.Domain.Budgets;

namespace BudgetManager.Infrastructure.Tests.Operations;

public sealed class OperationsServiceTests
{
    [Fact]
    public async Task Retention_legal_hold_never_applies_deletions()
    {
        var store = new RetentionStore(new RetentionConfiguration(30, 365, 730, 365, 2555, true));
        var service = new RetentionService(store);

        var result = await service.ExecuteAsync(Guid.NewGuid(), false);

        Assert.False(result.Applied);
        Assert.True(result.LegalHold);
        Assert.False(store.Applied);
        Assert.True(store.PreviewRecorded);
    }

    [Fact]
    public async Task Retention_dry_run_records_preview_without_applying_deletions()
    {
        var store = new RetentionStore(new RetentionConfiguration(30, 365, 730, 365, 2555, false));
        var service = new RetentionService(store);

        var result = await service.ExecuteAsync(Guid.NewGuid(), true);

        Assert.False(result.Applied);
        Assert.False(result.LegalHold);
        Assert.Equal(new RetentionPreview(1, 2, 3, 4, 5, 6), result.Preview);
        Assert.False(store.Applied);
        Assert.True(store.PreviewRecorded);
    }

    [Fact]
    public async Task Reconciliation_counts_each_safe_action_and_persists_decisions()
    {
        var store = new BaselineStore([
            new BaselineReconciliationItem(Guid.NewGuid(), "auto", 1250, 1000, 1250, BaselineMode.ProtectedAutomatic),
            new BaselineReconciliationItem(Guid.NewGuid(), "approval", 1250, 1000, 1250, BaselineMode.ApprovalOnly),
            new BaselineReconciliationItem(Guid.NewGuid(), "drift", 1300, 1000, 1250, BaselineMode.ProtectedAutomatic),
        ]);
        var service = new BaselineReconciliationService(store);

        var result = await service.ReconcileAsync(Guid.NewGuid(), false);

        Assert.Equal(1, result.AutomaticRequests);
        Assert.Equal(1, result.ApprovalRequests);
        Assert.Equal(1, result.ManualDrift);
        Assert.NotNull(store.Decisions);
    }

    private sealed class RetentionStore(RetentionConfiguration configuration) : IRetentionStore
    {
        public bool Applied { get; private set; }
        public bool PreviewRecorded { get; private set; }
        public Task<RetentionConfiguration?> GetConfigurationAsync(Guid enterpriseId, CancellationToken cancellationToken = default) => Task.FromResult<RetentionConfiguration?>(configuration);
        public Task<RetentionPreview> PreviewAsync(Guid enterpriseId, RetentionConfiguration settings, DateTimeOffset evaluatedAt, CancellationToken cancellationToken = default) => Task.FromResult(new RetentionPreview(1, 2, 3, 4, 5, 6));
        public Task RecordPreviewAsync(Guid enterpriseId, RetentionConfiguration settings, RetentionPreview preview, DateTimeOffset evaluatedAt, bool dryRun, CancellationToken cancellationToken = default) { PreviewRecorded = true; return Task.CompletedTask; }
        public Task ApplyAsync(Guid enterpriseId, RetentionConfiguration settings, DateTimeOffset evaluatedAt, CancellationToken cancellationToken = default) { Applied = true; return Task.CompletedTask; }
    }

    private sealed class BaselineStore(IReadOnlyList<BaselineReconciliationItem> items) : IBaselineReconciliationStore
    {
        public IReadOnlyList<(BaselineReconciliationItem Item, BaselineReconciliationDecision Decision)>? Decisions { get; private set; }
        public Task<IReadOnlyList<BaselineReconciliationItem>> GetItemsAsync(Guid enterpriseId, CancellationToken cancellationToken = default) => Task.FromResult(items);
        public Task SaveDecisionsAsync(Guid enterpriseId, IReadOnlyList<(BaselineReconciliationItem Item, BaselineReconciliationDecision Decision)> decisions, DateTimeOffset evaluatedAt, bool dryRun, CancellationToken cancellationToken = default) { Decisions = decisions; return Task.CompletedTask; }
    }
}
