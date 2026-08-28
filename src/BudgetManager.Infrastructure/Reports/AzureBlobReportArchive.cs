using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BudgetManager.Application.Reports;

namespace BudgetManager.Infrastructure.Reports;

public sealed class AzureBlobReportArchive(BlobContainerClient containerClient) : IReportArchive
{
    public async Task<string> SaveAsync(
        ReportArchiveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var safeSlug = string.Concat(request.EnterpriseSlug.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
        var blobName = $"raw/{safeSlug}/{request.ReportKind}/" +
            $"{request.ReportStartDay:yyyy-MM-dd}_{request.ReportEndDay:yyyy-MM-dd}/" +
            $"{request.ContentSha256.ToLowerInvariant()}.ndjson";
        var blobClient = containerClient.GetBlobClient(blobName);
        using var content = new MemoryStream(request.Content.ToArray(), writable: false);
        await blobClient.UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/x-ndjson" },
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["enterpriseId"] = request.EnterpriseId.ToString("N"),
                    ["reportKind"] = request.ReportKind.ToString(),
                    ["sha256"] = request.ContentSha256,
                },
                Conditions = null,
            },
            cancellationToken);
        return blobName;
    }
}
