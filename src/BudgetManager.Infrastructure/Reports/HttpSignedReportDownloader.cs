using BudgetManager.Application.Reports;

namespace BudgetManager.Infrastructure.Reports;

public sealed class HttpSignedReportDownloader(
    HttpClient httpClient,
    SignedReportDownloadOptions options) : ISignedReportDownloader
{
    public async Task<DownloadedReport> DownloadAsync(
        Uri source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.IsAbsoluteUri || source.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Signed report URL must be an absolute HTTPS URI.");
        }

        if (!options.AllowedHostSuffixes.Any(suffix =>
                source.Host.Equals(suffix, StringComparison.OrdinalIgnoreCase)
                || source.Host.EndsWith($".{suffix}", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Signed report URL host is not allowlisted.");
        }

        using var response = await httpClient.GetAsync(
            source,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > options.MaximumBytes)
        {
            throw new InvalidOperationException("Signed report exceeds the configured size limit.");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[81_920];
        long totalBytes = 0;
        while (true)
        {
            var bytesRead = await responseStream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            totalBytes += bytesRead;
            if (totalBytes > options.MaximumBytes)
            {
                throw new InvalidOperationException("Signed report exceeds the configured size limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        return new DownloadedReport(source, output.ToArray());
    }
}
