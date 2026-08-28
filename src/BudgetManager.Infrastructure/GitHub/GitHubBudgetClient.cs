using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BudgetManager.Application.GitHub;

namespace BudgetManager.Infrastructure.GitHub;

public sealed class GitHubBudgetClient : IGitHubBudgetClient
{
    private const string AcceptMediaType = "application/vnd.github+json";
    private const string ApiVersionHeader = "X-GitHub-Api-Version";
    private const string RequestIdHeader = "X-GitHub-Request-Id";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly HttpClient httpClient;
    private readonly IGitHubAccessTokenProvider tokenProvider;
    private readonly GitHubApiOptions options;

    public GitHubBudgetClient(
        HttpClient httpClient,
        IGitHubAccessTokenProvider tokenProvider,
        GitHubApiOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(tokenProvider);

        this.httpClient = httpClient;
        this.tokenProvider = tokenProvider;
        this.options = options ?? new GitHubApiOptions();
    }

    public async Task<GitHubBudgetPage> GetBudgetsAsync(
        string enterpriseSlug,
        int page = 1,
        int perPage = 100,
        CancellationToken cancellationToken = default)
    {
        ValidateEnterpriseSlug(enterpriseSlug);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);
        ArgumentOutOfRangeException.ThrowIfLessThan(perPage, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(perPage, 100);

        var path = $"enterprises/{Escape(enterpriseSlug)}/settings/billing/budgets?page={page}&per_page={perPage}";
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        return await ReadRequiredAsync<GitHubBudgetPage>(response, cancellationToken);
    }

    public async Task<GitHubBudget> GetBudgetAsync(
        string enterpriseSlug,
        string budgetId,
        CancellationToken cancellationToken = default)
    {
        ValidateEnterpriseSlug(enterpriseSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(budgetId);

        var path = $"enterprises/{Escape(enterpriseSlug)}/settings/billing/budgets/{Escape(budgetId)}";
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        return await ReadRequiredAsync<GitHubBudget>(response, cancellationToken);
    }

    public async Task<GitHubBudgetUpdateResult> UpdateBudgetAmountAsync(
        string enterpriseSlug,
        string budgetId,
        long budgetAmount,
        CancellationToken cancellationToken = default)
    {
        ValidateEnterpriseSlug(enterpriseSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(budgetId);
        ArgumentOutOfRangeException.ThrowIfNegative(budgetAmount);

        var path = $"enterprises/{Escape(enterpriseSlug)}/settings/billing/budgets/{Escape(budgetId)}";
        var payload = new UpdateBudgetAmountRequest(budgetAmount);
        using var response = await SendAsync(HttpMethod.Patch, path, payload, cancellationToken);
        return await ReadRequiredAsync<GitHubBudgetUpdateResult>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativePath,
        object? payload,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("GitHub access token provider returned an empty token.");
        }

        using var request = new HttpRequestMessage(method, new Uri(options.BaseAddress, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(AcceptMediaType));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(ApiVersionHeader, options.ApiVersion);
        request.Headers.UserAgent.ParseAdd("github-copilot-budget-manager/0.1");

        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var exception = await CreateApiExceptionAsync(response, cancellationToken);
            response.Dispose();
            throw exception;
        }

        return response;
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new GitHubApiException(
            "GitHub returned an empty or invalid response.",
            response.StatusCode,
            GetRequestId(response));
    }

    private static async Task<GitHubApiException> CreateApiExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        GitHubErrorResponse? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<GitHubErrorResponse>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
        }

        return new GitHubApiException(
            error?.Message ?? $"GitHub API returned HTTP {(int)response.StatusCode}.",
            response.StatusCode,
            GetRequestId(response),
            error?.DocumentationUrl);
    }

    private static string? GetRequestId(HttpResponseMessage response) =>
        response.Headers.TryGetValues(RequestIdHeader, out var requestIds)
            ? requestIds.FirstOrDefault()
            : null;

    private static void ValidateEnterpriseSlug(string enterpriseSlug) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(enterpriseSlug);

    private static string Escape(string pathSegment) => Uri.EscapeDataString(pathSegment);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record UpdateBudgetAmountRequest(long BudgetAmount);

    private sealed record GitHubErrorResponse(string Message, string? DocumentationUrl);
}
