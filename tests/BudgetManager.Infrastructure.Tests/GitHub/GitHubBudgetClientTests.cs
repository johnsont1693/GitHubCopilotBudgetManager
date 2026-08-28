using System.Net;
using BudgetManager.Application.GitHub;
using BudgetManager.Infrastructure.GitHub;

namespace BudgetManager.Infrastructure.Tests.GitHub;

public sealed class GitHubBudgetClientTests
{
    private static readonly GitHubApiOptions Options = new(new Uri("https://api.github.test/"));

    [Fact]
    public async Task GetBudgetsAsync_sends_required_headers_and_maps_the_page()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(
                "https://api.github.test/enterprises/acme/settings/billing/budgets?page=2&per_page=50",
                request.RequestUri?.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("installation-token", request.Headers.Authorization?.Parameter);
            Assert.Contains(request.Headers.Accept, item => item.MediaType == "application/vnd.github+json");
            Assert.Equal("2026-03-10", Assert.Single(request.Headers.GetValues("X-GitHub-Api-Version")));

            await Task.CompletedTask;
            return JsonResponse(HttpStatusCode.OK, """
                {
                  "budgets": [
                    {
                      "id": "budget-1",
                      "budget_type": "BundlePricing",
                      "budget_amount": 1000,
                      "prevent_further_usage": true,
                      "budget_scope": "enterprise",
                      "budget_entity_name": "",
                      "consumed_amount": 425.5,
                      "budget_product_sku": "ai_credits",
                      "budget_alerting": {
                        "will_alert": true,
                        "alert_recipients": ["octocat"]
                      }
                    }
                  ],
                  "effective_budget": {
                    "id": "budget-1",
                    "budget_amount": 1000,
                    "consumed_amount": 425.5
                  },
                  "has_next_page": true,
                  "total_count": 51
                }
                """);
        }));
        var client = new GitHubBudgetClient(httpClient, new FixedTokenProvider(), Options);

        var page = await client.GetBudgetsAsync("acme", page: 2, perPage: 50);

        var budget = Assert.Single(page.Budgets);
        Assert.Equal("budget-1", budget.Id);
        Assert.Equal(1_000, budget.BudgetAmount);
        Assert.Equal(425.5m, budget.ConsumedAmount);
        Assert.True(budget.BudgetAlerting.WillAlert);
        Assert.True(page.HasNextPage);
        Assert.Equal(51, page.TotalCount);
        Assert.Equal("budget-1", page.EffectiveBudget?.Id);
    }

    [Fact]
    public async Task UpdateBudgetAmountAsync_sends_a_minimal_patch_and_maps_the_result()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal(
                "https://api.github.test/enterprises/acme/settings/billing/budgets/budget-1",
                request.RequestUri?.AbsoluteUri);
            Assert.Equal("{\"budget_amount\":1250}", await request.Content!.ReadAsStringAsync());

            return JsonResponse(HttpStatusCode.OK, """
                {
                  "message": "Budget updated successfully",
                  "budget": {
                    "id": "budget-1",
                    "budget_type": "BundlePricing",
                    "budget_amount": 1250,
                    "prevent_further_usage": true,
                    "budget_scope": "enterprise",
                    "budget_entity_name": "",
                    "consumed_amount": 425.5,
                    "budget_product_sku": "ai_credits",
                    "budget_alerting": {
                      "will_alert": false,
                      "alert_recipients": []
                    }
                  }
                }
                """);
        }));
        var client = new GitHubBudgetClient(httpClient, new FixedTokenProvider(), Options);

        var result = await client.UpdateBudgetAmountAsync("acme", "budget-1", 1_250);

        Assert.Equal("Budget updated successfully", result.Message);
        Assert.Equal(1_250, result.Budget.BudgetAmount);
    }

    [Fact]
    public async Task GetBudgetAsync_preserves_safe_error_details_and_request_id()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var response = JsonResponse(HttpStatusCode.Forbidden, """
                {
                  "message": "Resource not accessible by integration",
                  "documentation_url": "https://docs.github.com/rest/billing/budgets"
                }
                """);
            response.Headers.Add("X-GitHub-Request-Id", "request-123");
            return Task.FromResult(response);
        }));
        var client = new GitHubBudgetClient(httpClient, new FixedTokenProvider(), Options);

        var exception = await Assert.ThrowsAsync<GitHubApiException>(
            () => client.GetBudgetAsync("acme", "budget-1"));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Equal("Resource not accessible by integration", exception.Message);
        Assert.Equal("request-123", exception.RequestId);
        Assert.Equal("https://docs.github.com/rest/billing/budgets", exception.DocumentationUrl);
    }

    [Fact]
    public void Options_reject_non_https_base_addresses()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new GitHubApiOptions(new Uri("http://api.github.test/")));

        Assert.Contains("HTTPS", exception.Message, StringComparison.Ordinal);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
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
