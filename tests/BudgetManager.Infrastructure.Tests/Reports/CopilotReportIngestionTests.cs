using System.Text;
using BudgetManager.Application.GitHub;
using BudgetManager.Application.Reports;
using BudgetManager.Infrastructure.Reports;

namespace BudgetManager.Infrastructure.Tests.Reports;

public sealed class CopilotReportIngestionTests
{
    private static readonly DateOnly ReportDay = new(2026, 8, 23);
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parser_normalizes_curated_user_and_enterprise_metrics()
    {
        var content = Encoding.UTF8.GetBytes("""
            {"day":"2026-08-23","user_login":"octocat","ai_credits_used":12,"used_agent":true,"ignored_id":99}
            {"day":"2026-08-23","daily_active_users":100}

            """);

        var parsed = new CopilotNdjsonMetricParser().Parse(
            content,
            "acme",
            CopilotMetricReportKind.UsersDay,
            ReportDay);

        Assert.Equal(3, parsed.Metrics.Count);
        Assert.Contains(parsed.Metrics, item => item.ScopeKind == "User"
            && item.ScopeExternalId == "octocat"
            && item.MetricKey == "ai_credits_used"
            && item.MetricValue == 12m);
        Assert.Contains(parsed.Metrics, item => item.MetricKey == "used_agent" && item.MetricValue == 1m);
        Assert.Contains(parsed.Metrics, item => item.ScopeKind == "Enterprise"
            && item.MetricKey == "daily_active_users");
        Assert.Contains(parsed.Entities, item => item.ScopeKind == "User" && item.ExternalId == "octocat");
    }

    [Fact]
    public void Parser_preserves_repository_and_team_hierarchy()
    {
        var content = Encoding.UTF8.GetBytes("""
            {"day":"2026-08-23","organization_id":"2002","repo_id":9001,"repo_owner_name":"acme","repo_name":"service","repo_visibility":"INTERNAL","pull_requests":{"total_merged":4,"median_minutes_to_merge":72.5}}
            {"day":"2026-08-23","enterprise_id":"1","user_id":1001,"user_login":"octocat","team_id":42,"slug":"platform"}
            """);

        var parsed = new CopilotNdjsonMetricParser().Parse(
            content,
            "acme-enterprise",
            CopilotMetricReportKind.UserTeamsDay,
            ReportDay);

        Assert.Contains(parsed.Entities, item => item.ScopeKind == "Organization" && item.ExternalId == "2002");
        Assert.Contains(parsed.Entities, item => item.ScopeKind == "Repository" && item.DisplayName == "acme/service");
        Assert.Contains(parsed.Entities, item => item.ScopeKind == "Team" && item.ExternalId == "42");
        Assert.Contains(parsed.Metrics, item => item.ScopeKind == "Repository"
            && item.MetricKey == "pull_requests.total_merged"
            && item.MetricValue == 4m);
        var membership = Assert.Single(parsed.Memberships);
        Assert.Equal("42", membership.ParentExternalId);
        Assert.Equal("octocat", membership.MemberExternalId);
    }

    [Fact]
    public async Task Downloader_rejects_a_non_allowlisted_host_before_sending()
    {
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        var downloader = new HttpSignedReportDownloader(
            httpClient,
            new SignedReportDownloadOptions(["reports.github.test"]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => downloader.DownloadAsync(new Uri("https://attacker.example/report.ndjson")));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Ingestion_archives_parses_and_persists_each_report_file()
    {
        var content = Encoding.UTF8.GetBytes(
            "{\"day\":\"2026-08-23\",\"user_login\":\"octocat\",\"ai_credits_used\":12}\n");
        var archive = new RecordingArchive();
        var store = new RecordingStore();
        var service = new CopilotReportIngestionService(
            new FixedEnterpriseClient(new GitHubCopilotReport(
                [new Uri("https://reports.github.test/report.ndjson")],
                ReportDay,
                null,
                null)),
            new FixedDownloader(content),
            archive,
            new CopilotNdjsonMetricParser(),
            store,
            new FixedTimeProvider(Now));

        var result = await service.IngestAsync(
            Guid.NewGuid(),
            "acme",
            CopilotMetricReportKind.UsersDay,
            ReportDay);

        Assert.Equal(1, result.FileCount);
        Assert.Equal(1, result.MetricCount);
        Assert.NotNull(archive.Request);
        Assert.NotNull(store.Batch);
        Assert.True(store.Batch.ReplaceExisting);
        Assert.Contains(store.Batch.Entities, item => item.ScopeKind == "User" && item.ExternalId == "octocat");
        Assert.Equal("raw/test.ndjson", Assert.Single(result.BlobNames));
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private sealed class FixedEnterpriseClient(GitHubCopilotReport report) : IGitHubEnterpriseClient
    {
        public Task<GitHubCopilotReport> GetCopilotMetricReportAsync(
            string enterpriseSlug,
            CopilotMetricReportKind reportKind,
            DateOnly? day = null,
            CancellationToken cancellationToken = default) => Task.FromResult(report);

        public Task<GitHubCostCenterList> GetCostCentersAsync(string enterpriseSlug, string? state = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GitHubUsageReportExportList> GetUsageReportExportsAsync(string enterpriseSlug, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GitHubUsageReportExport> CreateUsageReportExportAsync(string enterpriseSlug, string reportType, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GitHubUsageReportExport> GetUsageReportExportAsync(string enterpriseSlug, Guid reportId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GitHubBudgetUserStatePage> GetBudgetUserStatesAsync(string enterpriseSlug, string budgetId, int page = 1, int perPage = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedDownloader(byte[] content) : ISignedReportDownloader
    {
        public Task<DownloadedReport> DownloadAsync(Uri source, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DownloadedReport(source, content));
    }

    private sealed class RecordingArchive : IReportArchive
    {
        public ReportArchiveRequest? Request { get; private set; }

        public Task<string> SaveAsync(ReportArchiveRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult("raw/test.ndjson");
        }
    }

    private sealed class RecordingStore : IReportIngestionStore
    {
        public ReportIngestionBatch? Batch { get; private set; }

        public Task SaveAsync(ReportIngestionBatch batch, CancellationToken cancellationToken = default)
        {
            Batch = batch;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
