namespace BudgetManager.Application.Notifications;

public sealed record HealthNotificationSignal(
    Guid SnapshotId,
    string ScopeKind,
    string ScopeExternalId,
    string Status,
    decimal? Score,
    DateTimeOffset EvaluatedAt);

public sealed record NotificationRecipients(
    IReadOnlyList<string> UserPrincipalNames,
    IReadOnlyList<string> OwnerPrincipalNames);

public sealed record NotificationDeliveryOptions(IReadOnlyList<string> Channels)
{
    public static NotificationDeliveryOptions Default { get; } = new(["Teams", "Outlook"]);
}

public sealed record PlannedHealthNotification(
    HealthNotificationSignal Signal,
    NotificationRecipients Recipients,
    IReadOnlyList<string> AdminPrincipalNames,
    string EventFingerprint,
    string Subject,
    string Summary,
    string? DashboardUrl,
    DateTimeOffset PlannedAt);

public sealed record HealthNotificationPlanningResult(int Evaluated, int Planned, int SkippedNoRecipient);

public interface IHealthNotificationStore
{
    Task<IReadOnlyList<HealthNotificationSignal>> GetSignalsAsync(Guid enterpriseId, CancellationToken cancellationToken = default);

    Task<NotificationRecipients> ResolveRecipientsAsync(
        Guid enterpriseId,
        HealthNotificationSignal signal,
        CancellationToken cancellationToken = default);

    Task<bool> SaveAsync(
        Guid enterpriseId,
        PlannedHealthNotification notification,
        CancellationToken cancellationToken = default);
}

public sealed class HealthNotificationPlanner
{
    private readonly IHealthNotificationStore store;
    private readonly TimeProvider timeProvider;

    public HealthNotificationPlanner(IHealthNotificationStore store, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<HealthNotificationPlanningResult> PlanAsync(
        Guid enterpriseId,
        IReadOnlyList<string> adminPrincipalNames,
        string? dashboardUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(enterpriseId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(adminPrincipalNames);
        var signals = await store.GetSignalsAsync(enterpriseId, cancellationToken);
        var planned = 0;
        var skippedNoRecipient = 0;

        foreach (var signal in signals)
        {
            var recipients = await store.ResolveRecipientsAsync(enterpriseId, signal, cancellationToken);
            if (recipients.UserPrincipalNames.Count == 0
                && recipients.OwnerPrincipalNames.Count == 0
                && adminPrincipalNames.Count == 0)
            {
                skippedNoRecipient++;
                continue;
            }

            var notification = new PlannedHealthNotification(
                signal,
                recipients,
                adminPrincipalNames,
                $"health:{signal.SnapshotId:N}:{signal.Status.ToLowerInvariant()}",
                $"Copilot usage health is {signal.Status}",
                $"{signal.ScopeKind} {signal.ScopeExternalId} was classified {signal.Status}.",
                dashboardUrl,
                timeProvider.GetUtcNow());
            if (await store.SaveAsync(enterpriseId, notification, cancellationToken))
            {
                planned++;
            }
        }

        return new HealthNotificationPlanningResult(signals.Count, planned, skippedNoRecipient);
    }
}
