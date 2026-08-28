using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BudgetManager.Application.GitHub;

namespace BudgetManager.Infrastructure.GitHub;

public sealed class GitHubEnterpriseClient : IGitHubEnterpriseClient
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly HttpClient httpClient;
    private readonly IGitHubAccessTokenProvider tokenProvider;
    private readonly GitHubApiOptions options;

    public GitHubEnterpriseClient(
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

    public async Task<GitHubCopilotReport> GetCopilotMetricReportAsync(
        string enterpriseSlug,
        CopilotMetricReportKind reportKind,
        DateOnly? day = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSlug(enterpriseSlug);
        var (reportPath, requiresDay) = reportKind switch
        {
            CopilotMetricReportKind.EnterpriseDay => ("enterprise-1-day", true),
            CopilotMetricReportKind.EnterpriseLatest28Days => ("enterprise-28-day/latest", false),
            CopilotMetricReportKind.UsersDay => ("users-1-day", true),
            CopilotMetricReportKind.UsersLatest28Days => ("users-28-day/latest", false),
            CopilotMetricReportKind.RepositoriesDay => ("repos-1-day", true),
            CopilotMetricReportKind.UserTeamsDay => ("user-teams-1-day", true),
            _ => throw new ArgumentOutOfRangeException(nameof(reportKind)),
        };

        if (requiresDay && day is null)
        {
            throw new ArgumentException("A report day is required for one-day reports.", nameof(day));
        }

        if (!requiresDay && day is not null)
        {
            throw new ArgumentException("A report day is not accepted for latest rolling reports.", nameof(day));
        }

        var path = $"enterprises/{Escape(enterpriseSlug)}/copilot/metrics/reports/{reportPath}";
        if (day is not null)
        {
            path += $"?day={day:yyyy-MM-dd}";
        }

        return await SendAndReadAsync<GitHubCopilotReport>(HttpMethod.Get, path, null, cancellationToken);
    }

    public Task<GitHubCostCenterList> GetCostCentersAsync(
        string enterpriseSlug,
        string? state = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSlug(enterpriseSlug);
        if (state is not null && state is not ("active" or "deleted"))
        {
            throw new ArgumentException("Cost center state must be active or deleted.", nameof(state));
        }

        var path = $"enterprises/{Escape(enterpriseSlug)}/settings/billing/cost-centers";
        if (state is not null)
        {
            path += $"?state={state}";
        }

        return SendAndReadAsync<GitHubCostCenterList>(HttpMethod.Get, path, null, cancellationToken);
    }

    public Task<GitHubUsageReportExportList> GetUsageReportExportsAsync(
        string enterpriseSlug,
        CancellationToken cancellationToken = default)
    {
        ValidateSlug(enterpriseSlug);
        var path = $"enterprises/{Escape(enterpriseSlug)}/settings/billing/reports";
        return SendAndReadAsync<GitHubUsageReportExportList>(
            HttpMethod.Get,
            path,
            null,
            cancellationToken);
    }

    public Task<GitHubUsageReportExport> CreateUsageReportExportAsync(
        string enterpriseSlug,
        string reportType,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        ValidateSlug(enterpriseSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportType);
        if (endDate < startDate)
        {
            throw new ArgumentException("End date must not precede start date.", nameof(endDate));
        }

        var path = $"enterprises/{Escape(enterpriseSlug)}/settings/billing/reports";
        var request = new CreateUsageReportRequest(reportType, startDate, endDate, false);
        return SendAndReadAsync<GitHubUsageReportExport>(HttpMethod.Post, path, request, cancellationToken);
    }

    public Task<GitHubUsageReportExport> GetUsageReportExportAsync(
        string enterpriseSlug,
        Guid reportId,
        CancellationToken cancellationToken = default)
    {
        ValidateSlug(enterpriseSlug);
        ArgumentOutOfRangeException.ThrowIfEqual(reportId, Guid.Empty);
        var path = $"enterprises/{Escape(enterpriseSlug)}/settings/billing/reports/{reportId:D}";
        return SendAndReadAsync<GitHubUsageReportExport>(HttpMethod.Get, path, null, cancellationToken);
    }

    public Task<GitHubBudgetUserStatePage> GetBudgetUserStatesAsync(
        string enterpriseSlug,
        string budgetId,
        int page = 1,
        int perPage = 100,
        CancellationToken cancellationToken = default)
    {
        ValidateSlug(enterpriseSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(budgetId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);
        ArgumentOutOfRangeException.ThrowIfLessThan(perPage, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(perPage, 100);
        var path = $"enterprises/{Escape(enterpriseSlug)}/settings/billing/budgets/{Escape(budgetId)}" +
            $"/user-states?page={page}&per_page={perPage}";
        return SendAndReadAsync<GitHubBudgetUserStatePage>(HttpMethod.Get, path, null, cancellationToken);
    }

    private async Task<T> SendAndReadAsync<T>(
        HttpMethod method,
        string path,
        object? payload,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("GitHub access token provider returned an empty token.");
        }

        using var request = new HttpRequestMessage(method, new Uri(options.BaseAddress, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-GitHub-Api-Version", options.ApiVersion);
        request.Headers.UserAgent.ParseAdd("github-copilot-budget-manager/0.1");
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubApiException(
                $"GitHub enterprise API returned HTTP {(int)response.StatusCode}.",
                response.StatusCode,
                response.Headers.TryGetValues("X-GitHub-Request-Id", out var requestIds)
                    ? requestIds.FirstOrDefault()
                    : null);
        }

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new GitHubApiException(
            "GitHub returned an empty or invalid enterprise response.",
            response.StatusCode,
            null);
    }

    private static void ValidateSlug(string enterpriseSlug) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(enterpriseSlug);

    private static string Escape(string segment) => Uri.EscapeDataString(segment);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record CreateUsageReportRequest(
        string ReportType,
        DateOnly StartDate,
        DateOnly EndDate,
        bool SendEmail);
}
