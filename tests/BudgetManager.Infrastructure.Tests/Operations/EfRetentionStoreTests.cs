using BudgetManager.Application.Operations;
using BudgetManager.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Infrastructure.Tests.Operations;

public sealed class EfRetentionStoreTests
{
    [Fact]
    public async Task Preview_translates_all_cutoffs_on_sqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BudgetManagerDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new BudgetManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var store = new EfRetentionStore(context);
        var configuration = new RetentionConfiguration(30, 365, 730, 365, 2555, false);

        var preview = await store.PreviewAsync(
            Guid.NewGuid(),
            configuration,
            new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(new RetentionPreview(0, 0, 0, 0, 0, 0), preview);
    }

    [Fact]
    public async Task Apply_executes_all_cutoffs_on_sqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BudgetManagerDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new BudgetManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var store = new EfRetentionStore(context);
        var enterpriseId = Guid.NewGuid();
        var configuration = new RetentionConfiguration(30, 365, 730, 365, 2555, false);

        await store.ApplyAsync(
            enterpriseId,
            configuration,
            new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));

        var auditEvent = await context.AuditEvents.SingleAsync();
        Assert.Equal("retention.applied", auditEvent.EventType);
        Assert.Equal(enterpriseId, auditEvent.EnterpriseId);
    }
}
