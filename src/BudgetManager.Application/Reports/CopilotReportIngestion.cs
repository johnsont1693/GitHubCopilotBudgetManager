using BudgetManager.Application.GitHub;

namespace BudgetManager.Application.Reports;

public sealed record DownloadedReport(Uri Source, ReadOnlyMemory<byte> Content);

public sealed record NormalizedMetric(
    string ScopeKind,
    string ScopeExternalId,
    string MetricKey,
    decimal? MetricValue,
    string Availability,
    string? AvailabilityDetail,
    string Source,
    DateOnly MetricDay);

public sealed record NormalizedEntity(
    string ScopeKind,
    string ExternalId,
    string DisplayName,
    string? ParentScopeKind,
    string? ParentExternalId,
    string? MetadataJson);

public sealed record NormalizedMembership(
    string ParentScopeKind,
    string ParentExternalId,
    string MemberScopeKind,
    string MemberExternalId,
    DateOnly ObservedOn);

public sealed record ParsedCopilotReport(
    IReadOnlyList<NormalizedMetric> Metrics,
    IReadOnlyList<NormalizedEntity> Entities,
    IReadOnlyList<NormalizedMembership> Memberships);

public sealed record ReportArchiveRequest(
    Guid EnterpriseId,
    string EnterpriseSlug,
    CopilotMetricReportKind ReportKind,
    DateOnly ReportStartDay,
    DateOnly ReportEndDay,
    string ContentSha256,
    ReadOnlyMemory<byte> Content);

public sealed record ReportIngestionBatch(
    Guid EnterpriseId,
    string EnterpriseSlug,
    CopilotMetricReportKind ReportKind,
    DateOnly ReportStartDay,
    DateOnly ReportEndDay,
    string SourceUrlHash,
    string ContentSha256,
    string BlobName,
    bool ReplaceExisting,
    IReadOnlyList<NormalizedMetric> Metrics,
    IReadOnlyList<NormalizedEntity> Entities,
    IReadOnlyList<NormalizedMembership> Memberships,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public sealed record ReportIngestionResult(
    CopilotMetricReportKind ReportKind,
    DateOnly ReportStartDay,
    DateOnly ReportEndDay,
    int FileCount,
    int MetricCount,
    IReadOnlyList<string> BlobNames);

public interface ISignedReportDownloader
{
    Task<DownloadedReport> DownloadAsync(Uri source, CancellationToken cancellationToken = default);
}

public interface IReportArchive
{
    Task<string> SaveAsync(ReportArchiveRequest request, CancellationToken cancellationToken = default);
}

public interface ICopilotMetricParser
{
    ParsedCopilotReport Parse(
        ReadOnlyMemory<byte> content,
        string enterpriseSlug,
        CopilotMetricReportKind reportKind,
        DateOnly fallbackDay);
}

public interface IReportIngestionStore
{
    Task SaveAsync(ReportIngestionBatch batch, CancellationToken cancellationToken = default);
}

public sealed class CopilotReportIngestionService
{
    private readonly IGitHubEnterpriseClient enterpriseClient;
    private readonly ISignedReportDownloader downloader;
    private readonly IReportArchive archive;
    private readonly ICopilotMetricParser parser;
    private readonly IReportIngestionStore store;
    private readonly TimeProvider timeProvider;

    public CopilotReportIngestionService(
        IGitHubEnterpriseClient enterpriseClient,
        ISignedReportDownloader downloader,
        IReportArchive archive,
        ICopilotMetricParser parser,
        IReportIngestionStore store,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(enterpriseClient);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(store);

        this.enterpriseClient = enterpriseClient;
        this.downloader = downloader;
        this.archive = archive;
        this.parser = parser;
        this.store = store;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ReportIngestionResult> IngestAsync(
        Guid enterpriseId,
        string enterpriseSlug,
        CopilotMetricReportKind reportKind,
        DateOnly? day = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(enterpriseId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(enterpriseSlug);
        var report = await enterpriseClient.GetCopilotMetricReportAsync(
            enterpriseSlug,
            reportKind,
            day,
            cancellationToken);
        var reportStartDay = report.ReportDay ?? report.ReportStartDay
            ?? throw new InvalidOperationException("GitHub report response omitted its start day.");
        var reportEndDay = report.ReportDay ?? report.ReportEndDay
            ?? throw new InvalidOperationException("GitHub report response omitted its end day.");
        var blobNames = new List<string>();
        var metricCount = 0;
        var fileIndex = 0;

        foreach (var link in report.DownloadLinks)
        {
            var startedAt = timeProvider.GetUtcNow();
            var download = await downloader.DownloadAsync(link, cancellationToken);
            var contentSha256 = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(download.Content.Span));
            var sourceUrlHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(download.Source.AbsoluteUri)));
            var blobName = await archive.SaveAsync(new ReportArchiveRequest(
                enterpriseId,
                enterpriseSlug,
                reportKind,
                reportStartDay,
                reportEndDay,
                contentSha256,
                download.Content), cancellationToken);
            var parsed = parser.Parse(download.Content, enterpriseSlug, reportKind, reportStartDay);
            var completedAt = timeProvider.GetUtcNow();
            await store.SaveAsync(new ReportIngestionBatch(
                enterpriseId,
                enterpriseSlug,
                reportKind,
                reportStartDay,
                reportEndDay,
                sourceUrlHash,
                contentSha256,
                blobName,
                fileIndex == 0,
                parsed.Metrics,
                parsed.Entities,
                parsed.Memberships,
                startedAt,
                completedAt), cancellationToken);
            blobNames.Add(blobName);
            metricCount += parsed.Metrics.Count;
            fileIndex++;
        }

        return new ReportIngestionResult(
            reportKind,
            reportStartDay,
            reportEndDay,
            fileIndex,
            metricCount,
            blobNames.AsReadOnly());
    }
}
