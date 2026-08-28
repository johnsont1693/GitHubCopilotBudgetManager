using System.Net;
using System.Text;
using BudgetManager.Application.GitHub;
using BudgetManager.Infrastructure.GitHub;

namespace BudgetManager.Infrastructure.Tests.GitHub;

public sealed class GitHubEnterpriseClientTests
{
    private static readonly GitHubApiOptions Options = new(new Uri("https://api.github.test/"));

    [Theory]
    [InlineData(CopilotMetricReportKind.EnterpriseDay, "enterprise-1-day?day=2026-08-23")]
    [InlineData(CopilotMetricReportKind.UsersDay, "users-1-day?day=2026-08-23")]
    [InlineData(CopilotMetricReportKind.RepositoriesDay, "repos-1-day?day=2026-08-23")]
    [InlineData(CopilotMetricReportKind.UserTeamsDay, "user-teams-1-day?day=2026-08-23")]
    public async Task GetCopilotMetricReportAsync_maps_one_day_report_paths(
        CopilotMetricReportKind kind,
        string expectedSuffix)
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            Assert.EndsWith(expectedSuffix, request.RequestUri?.AbsoluteUri, StringComparison.Ordinal);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """
                {
                  "download_links": ["https://reports.github.test/report.ndjson"],
                  "report_day": "2026-08-23"
                }
                """));
        }));
        var client = CreateClient(httpClient);

        var report = await client.GetCopilotMetricReportAsync(
            "acme",
            kind,
            new DateOnly(2026, 8, 23));

        Assert.Equal(new DateOnly(2026, 8, 23), report.ReportDay);
        Assert.Equal("https://reports.github.test/report.ndjson", Assert.Single(report.DownloadLinks).AbsoluteUri);
    }

    [Fact]
    public async Task GetCostCentersAsync_maps_resources_and_pool_state()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            Assert.EndsWith("cost-centers?state=active", request.RequestUri?.AbsoluteUri, StringComparison.Ordinal);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """
                {
                  "costCenters": [
                    {
                      "id": "cc-1",
                      "name": "Engineering",
                      "state": "active",
                      "azure_subscription": null,
                      "ai_credit_pool_enabled": true,
                      "ai_credit_pool_state": { "target_amount": 1000, "current_amount": 450 },
                      "resources": [{ "type": "User", "name": "octocat" }]
                    }
                  ]
                }
                """));
        }));
        var client = CreateClient(httpClient);

        var result = await client.GetCostCentersAsync("acme", "active");

        var costCenter = Assert.Single(result.CostCenters);
        Assert.Equal("Engineering", costCenter.Name);
        Assert.Equal(450m, costCenter.AiCreditPoolState?.CurrentAmount);
        Assert.Equal("octocat", Assert.Single(costCenter.Resources).Name);
    }

    [Fact]
    public async Task CreateUsageReportExportAsync_sends_the_documented_payload()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                "{\"report_type\":\"detailed\",\"start_date\":\"2026-08-01\",\"end_date\":\"2026-08-23\",\"send_email\":false}",
                await request.Content!.ReadAsStringAsync());
            return JsonResponse(HttpStatusCode.Accepted, """
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "report_type": "detailed",
                  "start_date": "2026-08-01",
                  "end_date": "2026-08-23",
                  "status": "processing",
                  "download_urls": []
                }
                """);
        }));
        var client = CreateClient(httpClient);

        var report = await client.CreateUsageReportExportAsync(
            "acme",
            "detailed",
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 23));

        Assert.Equal("processing", report.Status);
    }

    [Fact]
    public async Task GetBudgetUserStatesAsync_maps_the_paged_response()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, """
                {
                  "user_states": [
                    {
                      "user": "octocat",
                      "consumed_amount": 20,
                      "target_amount": 30,
                      "override_budget_id": "override-1"
                    }
                  ],
                  "has_next_page": false,
                  "total_count": 1
                }
                """))));
        var client = CreateClient(httpClient);

        var result = await client.GetBudgetUserStatesAsync("acme", "budget-1");

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("octocat", Assert.Single(result.UserStates).User);
    }

    private static GitHubEnterpriseClient CreateClient(HttpClient httpClient) => new(
        httpClient,
        new FixedTokenProvider(),
        Options);

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class FixedTokenProvider : IGitHubAccessTokenProvider
    {
        public ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult("installation-token");
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request);
    }
}
