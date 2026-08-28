using System.Text.Json;
using BudgetManager.Application.Notifications;
using BudgetManager.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Infrastructure.Tests.Notifications;

public sealed class EfHealthNotificationStoreTests
{
    [Fact]
    public async Task GetSignalsAsync_ignores_an_old_red_when_the_latest_status_is_green()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        fixture.Context.ClassificationSnapshots.AddRange(
            fixture.CreateSnapshot("red", fixture.Now.AddHours(-2)),
            fixture.CreateSnapshot("green", fixture.Now));
        await fixture.Context.SaveChangesAsync();

        var signals = await fixture.Store.GetSignalsAsync(fixture.EnterpriseId);

        Assert.Empty(signals);
    }

    [Fact]
    public async Task SaveAsync_creates_outlook_only_for_user_level_health_events()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        var signal = new HealthNotificationSignal(Guid.NewGuid(), "User", "octocat", "yellow", 50m, fixture.Now);
        var notification = new PlannedHealthNotification(
            signal,
            new NotificationRecipients(["octocat@contoso.com"], ["owner@contoso.com"]),
            ["admin@contoso.com"],
            "health:user:yellow",
            "Health changed",
            "User health changed.",
            "https://dashboard.example/",
            fixture.Now);

        Assert.True(await fixture.Store.SaveAsync(fixture.EnterpriseId, notification));

        var deliveries = await fixture.Context.NotificationDeliveries.ToListAsync();
        Assert.Equal(3, deliveries.Count);
        Assert.All(deliveries, item => Assert.Equal("Outlook", item.Channel));
        var payload = await fixture.Context.OutboxMessages.Select(item => item.PayloadJson).SingleAsync();
        using var document = JsonDocument.Parse(payload);
        Assert.Equal("User", document.RootElement.GetProperty("payload").GetProperty("scopeKind").GetString());
    }

    [Fact]
    public async Task SaveAsync_can_create_outlook_only_for_aggregate_health_events()
    {
        await using var fixture = await StoreFixture.CreateAsync(
            new NotificationDeliveryOptions(["outlook"]));
        var signal = new HealthNotificationSignal(Guid.NewGuid(), "Organization", "acme", "red", 25m, fixture.Now);
        var notification = new PlannedHealthNotification(
            signal,
            new NotificationRecipients([], ["owner@contoso.com"]),
            ["admin@contoso.com"],
            "health:organization:red",
            "Health changed",
            "Organization health changed.",
            null,
            fixture.Now);

        Assert.True(await fixture.Store.SaveAsync(fixture.EnterpriseId, notification));

        var deliveries = await fixture.Context.NotificationDeliveries.ToListAsync();
        Assert.Equal(2, deliveries.Count);
        Assert.All(deliveries, item => Assert.Equal("Outlook", item.Channel));
    }

    [Fact]
    public async Task SaveAsync_preserves_teams_and_outlook_as_the_aggregate_default()
    {
        await using var fixture = await StoreFixture.CreateAsync();
        var signal = new HealthNotificationSignal(Guid.NewGuid(), "Organization", "acme", "yellow", 55m, fixture.Now);
        var notification = new PlannedHealthNotification(
            signal,
            new NotificationRecipients([], ["owner@contoso.com"]),
            ["admin@contoso.com"],
            "health:organization:yellow",
            "Health changed",
            "Organization health changed.",
            null,
            fixture.Now);

        Assert.True(await fixture.Store.SaveAsync(fixture.EnterpriseId, notification));

        var deliveries = await fixture.Context.NotificationDeliveries.ToListAsync();
        Assert.Equal(4, deliveries.Count);
        Assert.Equal(
            ["Outlook", "Teams"],
            deliveries.Select(item => item.Channel).Distinct().Order().ToArray());
    }

    private sealed class StoreFixture : IAsyncDisposable
    {
        private StoreFixture(
            SqliteConnection connection,
            BudgetManagerDbContext context,
            NotificationDeliveryOptions? deliveryOptions)
        {
            Connection = connection;
            Context = context;
            Store = new EfHealthNotificationStore(context, deliveryOptions);
        }

        public Guid EnterpriseId { get; } = Guid.NewGuid();
        public DateTimeOffset Now { get; } = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
        public SqliteConnection Connection { get; }
        public BudgetManagerDbContext Context { get; }
        public EfHealthNotificationStore Store { get; }

        public static async Task<StoreFixture> CreateAsync(
            NotificationDeliveryOptions? deliveryOptions = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new BudgetManagerDbContext(
                new DbContextOptionsBuilder<BudgetManagerDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new StoreFixture(connection, context, deliveryOptions);
        }

        public ClassificationSnapshotRecord CreateSnapshot(string status, DateTimeOffset evaluatedAt) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            ScopeKind = ScopeKind.User,
            ScopeExternalId = "octocat",
            PolicyId = Guid.NewGuid(),
            PolicyVersion = 1,
            Status = status,
            Score = status == "green" ? 80m : 20m,
            ReasonsJson = "[]",
            InputsFingerprint = $"{status}-{evaluatedAt:O}",
            EvaluatedAt = evaluatedAt,
        };

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
