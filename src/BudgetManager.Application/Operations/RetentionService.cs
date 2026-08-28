namespace BudgetManager.Application.Operations;

public sealed record RetentionConfiguration(
    int RawReportDays,
    int UserMetricDays,
    int AggregateMetricDays,
    int NotificationDays,
    int AuditDays,
    bool LegalHold);

public sealed record RetentionPreview(
    int IngestionManifests,
    int UserMetrics,
    int AggregateMetrics,
    int Classifications,
    int Notifications,
    int AuditEvents);

public sealed record RetentionExecutionResult(bool Applied, bool LegalHold, RetentionPreview Preview);

public interface IRetentionStore
{
    Task<RetentionConfiguration?> GetConfigurationAsync(Guid enterpriseId, CancellationToken cancellationToken = default);

    Task<RetentionPreview> PreviewAsync(
        Guid enterpriseId,
        RetentionConfiguration configuration,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken = default);

    Task RecordPreviewAsync(
        Guid enterpriseId,
        RetentionConfiguration configuration,
        RetentionPreview preview,
        DateTimeOffset evaluatedAt,
        bool dryRun,
        CancellationToken cancellationToken = default);

    Task ApplyAsync(
        Guid enterpriseId,
        RetentionConfiguration configuration,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken = default);
}

public sealed class RetentionService
{
    private readonly IRetentionStore store;
    private readonly TimeProvider timeProvider;

    public RetentionService(IRetentionStore store, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<RetentionExecutionResult> ExecuteAsync(
        Guid enterpriseId,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(enterpriseId, Guid.Empty);
        var configuration = await store.GetConfigurationAsync(enterpriseId, cancellationToken)
            ?? throw new InvalidOperationException("Retention settings must be configured before retention can run.");
        Validate(configuration);
        var now = timeProvider.GetUtcNow();
        var preview = await store.PreviewAsync(enterpriseId, configuration, now, cancellationToken);
        await store.RecordPreviewAsync(
            enterpriseId,
            configuration,
            preview,
            now,
            dryRun,
            cancellationToken);
        if (dryRun || configuration.LegalHold)
        {
            return new RetentionExecutionResult(false, configuration.LegalHold, preview);
        }

        await store.ApplyAsync(enterpriseId, configuration, now, cancellationToken);
        return new RetentionExecutionResult(true, false, preview);
    }

    private static void Validate(RetentionConfiguration configuration)
    {
        if (configuration.RawReportDays <= 0
            || configuration.UserMetricDays <= 0
            || configuration.AggregateMetricDays <= 0
            || configuration.NotificationDays <= 0
            || configuration.AuditDays <= 0)
        {
            throw new InvalidOperationException("Every retention duration must be greater than zero.");
        }
    }
}
