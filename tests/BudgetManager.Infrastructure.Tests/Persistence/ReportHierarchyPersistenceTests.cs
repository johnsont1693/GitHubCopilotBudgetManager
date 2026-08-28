using BudgetManager.Application.GitHub;
using BudgetManager.Application.Reports;
using BudgetManager.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BudgetManager.Infrastructure.Tests.Persistence;

public sealed class ReportHierarchyPersistenceTests
{
    [Fact]
    public async Task User_team_ingestion_persists_membership_and_derives_supported_team_metrics()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BudgetManagerDbContext>().UseSqlite(connection).Options;
        await using var context = new BudgetManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var enterpriseId = Guid.NewGuid();
        var day = new DateOnly(2026, 8, 23);
        var now = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
        var store = new EfReportIngestionStore(context);

        await store.SaveAsync(CreateBatch(
            enterpriseId,
            CopilotMetricReportKind.UsersDay,
            [
                new NormalizedMetric("User", "octocat", "code_generation_activity_count", 3m, "available", null, "copilot.UsersDay", day),
                new NormalizedMetric("User", "hubot", "code_generation_activity_count", 5m, "available", null, "copilot.UsersDay", day),
            ],
            [
                new NormalizedEntity("User", "octocat", "octocat", "Enterprise", "acme", null),
                new NormalizedEntity("User", "hubot", "hubot", "Enterprise", "acme", null),
            ],
            [],
            day,
            now));
        await store.SaveAsync(CreateBatch(
            enterpriseId,
            CopilotMetricReportKind.UserTeamsDay,
            [],
            [new NormalizedEntity("Team", "42", "platform", "Enterprise", "acme", null)],
            [
                new NormalizedMembership("Team", "42", "User", "octocat", day),
                new NormalizedMembership("Team", "42", "User", "hubot", day),
            ],
            day,
            now.AddMinutes(1)));

        Assert.Equal(3, await context.ManagedEntities.CountAsync());
        Assert.Equal(2, await context.EntityMemberships.CountAsync());
        var teamMetric = await context.DailyMetrics.SingleAsync(item => item.ScopeKind == ScopeKind.Team);
        Assert.Equal("42", teamMetric.ScopeExternalId);
        Assert.Equal(8m, teamMetric.MetricValue);
    }

    [Fact]
    public async Task Repository_ingestion_sums_additive_metrics_to_organization_but_not_medians()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BudgetManagerDbContext>().UseSqlite(connection).Options;
        await using var context = new BudgetManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var enterpriseId = Guid.NewGuid();
        var day = new DateOnly(2026, 8, 23);
        var now = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
        var store = new EfReportIngestionStore(context);

        await store.SaveAsync(CreateBatch(
            enterpriseId,
            CopilotMetricReportKind.RepositoriesDay,
            [
                new NormalizedMetric("Repository", "repo-1", "pull_requests.total_merged", 2m, "available", null, "copilot.RepositoriesDay", day),
                new NormalizedMetric("Repository", "repo-1", "pull_requests.median_minutes_to_merge", 60m, "available", null, "copilot.RepositoriesDay", day),
                new NormalizedMetric("Repository", "repo-2", "pull_requests.total_merged", 3m, "available", null, "copilot.RepositoriesDay", day),
                new NormalizedMetric("Repository", "repo-2", "pull_requests.median_minutes_to_merge", 90m, "available", null, "copilot.RepositoriesDay", day),
            ],
            [
                new NormalizedEntity("Organization", "org-1", "Acme", "Enterprise", "acme", null),
                new NormalizedEntity("Repository", "repo-1", "Acme/one", "Organization", "org-1", null),
                new NormalizedEntity("Repository", "repo-2", "Acme/two", "Organization", "org-1", null),
            ],
            [],
            day,
            now));

        var organizationMetrics = await context.DailyMetrics
            .Where(item => item.ScopeKind == ScopeKind.Organization)
            .ToListAsync();
        var aggregate = Assert.Single(organizationMetrics);
        Assert.Equal("pull_requests.total_merged", aggregate.MetricKey);
        Assert.Equal(5m, aggregate.MetricValue);
    }

    private static ReportIngestionBatch CreateBatch(
        Guid enterpriseId,
        CopilotMetricReportKind reportKind,
        IReadOnlyList<NormalizedMetric> metrics,
        IReadOnlyList<NormalizedEntity> entities,
        IReadOnlyList<NormalizedMembership> memberships,
        DateOnly day,
        DateTimeOffset now) => new(
            enterpriseId,
            "acme",
            reportKind,
            day,
            day,
            $"source-{reportKind}",
            $"content-{reportKind}",
            $"raw/{reportKind}.ndjson",
            true,
            metrics,
            entities,
            memberships,
            now,
            now);
}
