using System.Text.Json.Serialization;

namespace BudgetManager.Application.GitHub;

public enum CopilotMetricReportKind
{
    EnterpriseDay = 0,
    EnterpriseLatest28Days = 1,
    UsersDay = 2,
    UsersLatest28Days = 3,
    RepositoriesDay = 4,
    UserTeamsDay = 5,
}

public sealed record GitHubCopilotReport(
    IReadOnlyList<Uri> DownloadLinks,
    DateOnly? ReportDay,
    DateOnly? ReportStartDay,
    DateOnly? ReportEndDay);

public sealed record GitHubCostCenterResource(string Type, string Name);

public sealed record GitHubAiCreditPoolState(decimal? TargetAmount, decimal? CurrentAmount);

public sealed record GitHubCostCenter(
    string Id,
    string Name,
    string State,
    string? AzureSubscription,
    bool AiCreditPoolEnabled,
    GitHubAiCreditPoolState? AiCreditPoolState,
    IReadOnlyList<GitHubCostCenterResource> Resources);

public sealed record GitHubCostCenterList(
    [property: JsonPropertyName("costCenters")] IReadOnlyList<GitHubCostCenter> CostCenters);

public sealed record GitHubUsageReportExport(
    Guid Id,
    string ReportType,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    IReadOnlyList<Uri>? DownloadUrls,
    DateTimeOffset? CreatedAt,
    string? Actor);

public sealed record GitHubUsageReportExportList(
    IReadOnlyList<GitHubUsageReportExport> UsageReportExports);

public sealed record GitHubBudgetUserState(
    string? User,
    decimal ConsumedAmount,
    decimal TargetAmount,
    string? OverrideBudgetId);

public sealed record GitHubBudgetUserStatePage(
    IReadOnlyList<GitHubBudgetUserState> UserStates,
    bool HasNextPage,
    int TotalCount);

public interface IGitHubEnterpriseClient
{
    Task<GitHubCopilotReport> GetCopilotMetricReportAsync(
        string enterpriseSlug,
        CopilotMetricReportKind reportKind,
        DateOnly? day = null,
        CancellationToken cancellationToken = default);

    Task<GitHubCostCenterList> GetCostCentersAsync(
        string enterpriseSlug,
        string? state = null,
        CancellationToken cancellationToken = default);

    Task<GitHubUsageReportExportList> GetUsageReportExportsAsync(
        string enterpriseSlug,
        CancellationToken cancellationToken = default);

    Task<GitHubUsageReportExport> CreateUsageReportExportAsync(
        string enterpriseSlug,
        string reportType,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    Task<GitHubUsageReportExport> GetUsageReportExportAsync(
        string enterpriseSlug,
        Guid reportId,
        CancellationToken cancellationToken = default);

    Task<GitHubBudgetUserStatePage> GetBudgetUserStatesAsync(
        string enterpriseSlug,
        string budgetId,
        int page = 1,
        int perPage = 100,
        CancellationToken cancellationToken = default);
}
