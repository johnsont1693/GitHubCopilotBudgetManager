using BudgetManager.Application.Budgets;
using BudgetManager.Application.GitHub;
using BudgetManager.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Infrastructure.Tests.Persistence;

public sealed class BudgetSynchronizationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SynchronizeAsync_persists_all_pages_effective_budget_and_audit_event()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BudgetManagerDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new BudgetManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var enterpriseId = Guid.NewGuid();
        context.Enterprises.Add(new EnterpriseRecord
        {
            Id = enterpriseId,
            Slug = "acme",
            DisplayName = "Acme Enterprise",
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await context.SaveChangesAsync();

        var client = new PagedBudgetClient(
            new GitHubBudgetPage(
                [CreateBudget("budget-1", 1_000)],
                null,
                new GitHubEffectiveBudget("budget-2", 2_000, 1_100m),
                true,
                2),
            new GitHubBudgetPage(
                [CreateBudget("budget-2", 2_000)],
                null,
                new GitHubEffectiveBudget("budget-2", 2_000, 1_100m),
                false,
                2));
        var service = new BudgetSynchronizationService(
            client,
            new FinancialEnterpriseClient(),
            new EfBudgetSnapshotStore(context),
            new FixedTimeProvider(Now));

        var result = await service.SynchronizeAsync(enterpriseId, "acme");

        Assert.Equal(2, result.BudgetCount);
        Assert.Equal(2, result.PageCount);
        Assert.Equal("budget-2", result.EffectiveBudgetId);
        Assert.Equal(1, result.CostCenterCount);
        Assert.Equal(3, result.UserStateCount);
        Assert.Equal(1, result.UsageReportExportCount);
        Assert.Equal(2, await context.BudgetSnapshots.CountAsync());
        Assert.Equal(
            "budget-2",
            (await context.BudgetSnapshots.SingleAsync(item => item.IsEffective)).BudgetId);
        Assert.Equal(3, await context.BudgetUserStateSnapshots.CountAsync());
        Assert.Equal(1, await context.BillingReportExports.CountAsync());
        Assert.Equal(1, await context.EntityMemberships.CountAsync(item =>
            item.ParentScopeKind == ScopeKind.CostCenter
            && item.MemberScopeKind == ScopeKind.User));
        Assert.Equal(3, await context.DailyMetrics.CountAsync(item =>
            item.ScopeKind == ScopeKind.CostCenter));
        Assert.Equal(
            "github.budgets.synchronized",
            (await context.AuditEvents.SingleAsync()).EventType);
    }

    private static GitHubBudget CreateBudget(string id, long amount) => new(
        id,
        "BundlePricing",
        amount,
        true,
        "enterprise",
        string.Empty,
        null,
        amount / 2m,
        "ai_credits",
        new GitHubBudgetAlerting(false, []));

    private sealed class PagedBudgetClient(params GitHubBudgetPage[] pages) : IGitHubBudgetClient
    {
        public Task<GitHubBudgetPage> GetBudgetsAsync(
            string enterpriseSlug,
            int page = 1,
            int perPage = 100,
            CancellationToken cancellationToken = default) => Task.FromResult(pages[page - 1]);

        public Task<GitHubBudget> GetBudgetAsync(
            string enterpriseSlug,
            string budgetId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GitHubBudgetUpdateResult> UpdateBudgetAmountAsync(
            string enterpriseSlug,
            string budgetId,
            long budgetAmount,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FinancialEnterpriseClient : IGitHubEnterpriseClient
    {
        public Task<GitHubCostCenterList> GetCostCentersAsync(
            string enterpriseSlug,
            string? state = null,
            CancellationToken cancellationToken = default) => Task.FromResult(new GitHubCostCenterList(
            [
                new GitHubCostCenter(
                    "cc-1",
                    "Engineering",
                    "active",
                    null,
                    true,
                    new GitHubAiCreditPoolState(100m, 40m),
                    [new GitHubCostCenterResource("User", "octocat")]),
            ]));

        public Task<GitHubUsageReportExportList> GetUsageReportExportsAsync(
            string enterpriseSlug,
            CancellationToken cancellationToken = default) => Task.FromResult(new GitHubUsageReportExportList(
            [
                new GitHubUsageReportExport(
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    "detailed",
                    new DateOnly(2026, 8, 1),
                    new DateOnly(2026, 8, 23),
                    "ready",
                    [new Uri("https://signed.example/report.csv")],
                    Now.AddHours(-1),
                    "billing-admin"),
            ]));

        public Task<GitHubBudgetUserStatePage> GetBudgetUserStatesAsync(
            string enterpriseSlug,
            string budgetId,
            int page = 1,
            int perPage = 100,
            CancellationToken cancellationToken = default)
        {
            if (budgetId == "budget-1" && page == 1)
            {
                return Task.FromResult(new GitHubBudgetUserStatePage(
                    [new GitHubBudgetUserState("octocat", 20m, 30m, null)],
                    true,
                    2));
            }

            return Task.FromResult(new GitHubBudgetUserStatePage(
                [new GitHubBudgetUserState($"user-{budgetId}-{page}", 10m, 25m, null)],
                false,
                1));
        }

        public Task<GitHubCopilotReport> GetCopilotMetricReportAsync(string enterpriseSlug, CopilotMetricReportKind reportKind, DateOnly? day = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitHubUsageReportExport> CreateUsageReportExportAsync(string enterpriseSlug, string reportType, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitHubUsageReportExport> GetUsageReportExportAsync(string enterpriseSlug, Guid reportId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
