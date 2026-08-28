using BudgetManager.Application.Notifications;

namespace BudgetManager.Infrastructure.Tests.Notifications;

public sealed class HealthNotificationPlannerTests
{
    [Fact]
    public async Task PlanAsync_routes_to_user_owner_and_admin_and_deduplicates_through_store()
    {
        var signal = new HealthNotificationSignal(Guid.NewGuid(), "User", "octocat", "yellow", 55m, DateTimeOffset.UtcNow);
        var store = new RecordingStore(signal, new NotificationRecipients(["octocat@contoso.com"], ["owner@contoso.com"]));
        var planner = new HealthNotificationPlanner(store);

        var result = await planner.PlanAsync(Guid.NewGuid(), ["admin@contoso.com"], "https://dashboard.example");

        Assert.Equal(1, result.Planned);
        var notification = Assert.Single(store.Saved);
        Assert.Equal("octocat@contoso.com", Assert.Single(notification.Recipients.UserPrincipalNames));
        Assert.Equal("owner@contoso.com", Assert.Single(notification.Recipients.OwnerPrincipalNames));
        Assert.Equal("admin@contoso.com", Assert.Single(notification.AdminPrincipalNames));
    }

    [Fact]
    public async Task PlanAsync_does_not_require_a_dashboard()
    {
        var signal = new HealthNotificationSignal(Guid.NewGuid(), "User", "octocat", "red", 25m, DateTimeOffset.UtcNow);
        var store = new RecordingStore(signal, new NotificationRecipients([], []));
        var planner = new HealthNotificationPlanner(store);

        var result = await planner.PlanAsync(Guid.NewGuid(), ["admin@contoso.com"], null);

        Assert.Equal(1, result.Planned);
        Assert.Null(Assert.Single(store.Saved).DashboardUrl);
    }

    private sealed class RecordingStore(HealthNotificationSignal signal, NotificationRecipients recipients) : IHealthNotificationStore
    {
        public List<PlannedHealthNotification> Saved { get; } = [];
        public Task<IReadOnlyList<HealthNotificationSignal>> GetSignalsAsync(Guid enterpriseId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<HealthNotificationSignal>>([signal]);
        public Task<NotificationRecipients> ResolveRecipientsAsync(Guid enterpriseId, HealthNotificationSignal notificationSignal, CancellationToken cancellationToken = default) => Task.FromResult(recipients);
        public Task<bool> SaveAsync(Guid enterpriseId, PlannedHealthNotification notification, CancellationToken cancellationToken = default) { Saved.Add(notification); return Task.FromResult(true); }
    }
}
